/// <summary>
/// Connection Protocol v1.0
/// </summary>
public static class ConnectionProtocol {

    /// <summary>True to output debug messages, false otherwise</summary>
    private const bool PRINTDEBUG = true;// const to optimize compilation

    /// <summary>Antenna Reference</summary>
    private static IMyRadioAntenna antenna;

    /// <summary>Host name reference, used in packets to filter out received stream of data</summary>
    private static string hostId;

    /// <summary>Called when data was received from the specified connection</summary>
    private static Action<ConnectionImpl, string> onDataReceive;

    /// <summary>Called when a request to open an unencrypted connection is received</summary>
    private static Func<string, byte, bool> onConnectionRequest;

    /// <summary>Called when a request to open a secure connection is received</summary>
    private static Func<string, byte, byte[]> onSecureConnectionRequest;

    /// <summary>Called when a new connection is opened</summary>
    private static Action<IConnection> onConnectionOpen;

    /// <summary>Called when a connection is closed</summary>
    private static Action<IConnection> onConnectionClose;

    /// <summary>Maximal amount of times to send data packets before closing down related connection</summary>
    private static int maxSendCount;

    /// <summary>Stores all connections, connection at index 0 is used as a static connection, that is does not have any ids, used for connection opening and closing</summary>
    private static List<IBaseConnection> connections;

    /// <summary>Used to determine what connection to process in an update call</summary>
    private static int lastIteratedIndex = 0;

    /// <summary>Property indicating whether the protocol has been initialized for operation</summary>
    public static bool IsInitialized { get { return antenna != null && hostId != null; } }

    /// <summary>
    /// Hashes a string key into a byte array which can be used to open/accept a secure connection
    /// </summary>
    /// <param name="key">string key to hash</param>
    /// <returns>Hashed byte array key</returns>
    public static byte[] StringToHash(string key) {// this needs to be rewritten
        byte[] key0 = Encoding.Unicode.GetBytes(key);
        int l = key0.Length;
        byte lastByte = key0[l - 1];
        for (int i = 0, j = 1; i < l; i++, j++) {
            key0[i] = (byte)(key0[i] << (((i >> 0) & 0xff) ^ (j < l ? key0[j] : lastByte)));
        }
        return key0;
    }

    /// <summary>
    /// Main Initialization method that prepares the protocol for operation.
    /// </summary>
    /// <param name="antenna">Radio antenna to use in transmission</param>
    /// <param name="hostId">Identifier for this protocol instance (that is, a programming block)</param>
    /// <param name="onDataReceive">Called when data was received from the specified connection (connection object, data received)</param>
    /// <param name="onConnectionRequest">Called when a request to open an unencrypted connection is received (sender name, channel, return: accept[true], decline[false])</param>
    /// <param name="onSecureConnectionRequest">Called when a request to open a secure connection is received (sender name, channel, return: null[decline], crypto key to use)</param>
    /// <param name="onConnectionOpen">Called when a new connection is opened (connection object that is now active)</param>
    /// <param name="onConnectionClose">Called when a connection is closed (connection object that is no longer active)</param>
    /// <param name="maxPacketResendCount">Maximal amount of times to send data packets before closing down related connection</param>
    public static void Init(IMyRadioAntenna antenna, string hostId, Action<IConnection, string> onDataReceive, Func<string, byte, bool> onConnectionRequest, Func<string, byte, byte[]> onSecureConnectionRequest, Action<IConnection> onConnectionOpen, Action<IConnection> onConnectionClose, int maxPacketResendCount = 10) {
        Shutdown();// safeguard against already initialized protocol
        ConnectionProtocol.antenna = antenna;
        ConnectionProtocol.hostId = hostId;
        ConnectionProtocol.onDataReceive = onDataReceive;
        ConnectionProtocol.onConnectionRequest = onConnectionRequest;
        ConnectionProtocol.onSecureConnectionRequest = onSecureConnectionRequest;
        ConnectionProtocol.onConnectionOpen = onConnectionOpen;
        ConnectionProtocol.onConnectionClose = onConnectionClose;
        maxSendCount = maxPacketResendCount > 0 ? maxPacketResendCount : 1;// minimum of 1 send, which most probably will fail quickly
        connections = new List<IBaseConnection>();
        connections.Add(new StaticConnection());// initialize index 0 to the static connection
    }

    /// <summary>
    /// Shuts down this protocol and closes all related connections
    /// </summary>
    public static void Shutdown() {
        antenna = null;
        hostId = null;
        onDataReceive = null;
        onConnectionRequest = null;
        onSecureConnectionRequest = null;
        onConnectionOpen = null;
        onConnectionClose = null;
        if (connections != null) {
            for (int i = 1, l = connections.Count; i < l; i++) {// close all user connections
                connections[i].Close();
            }
            QueueList<BasePacket> staticPackets = ((StaticConnection)connections[0]).staticMessages;
            if (staticPackets != null) {
                while (staticPackets.Count > 0) {// remove any added packets
                    staticPackets.Dequeue().ClearPacketData();
                }
            }
            connections.Clear();
            connections = null;
        }
        lastIteratedIndex = 0;
    }

    /// <summary>
    /// Main update method to be frequently run, sends out queued packets if there are any.
    /// </summary>
    public static void UpdateLogic() {
        int l = connections.Count;
        if (l > 0) {
            int iterations = l;// maximal amount of iterations there can be
            while (true) {// while loop acts like a label here
                if (++lastIteratedIndex >= l) { lastIteratedIndex = 0; }// 

                IBaseConnection connection = connections[lastIteratedIndex];
                QueueList<BasePacket> packetQueue;

                // iterate over connections until one with unsent packets is found, or all connections have been iterated over
                while ((packetQueue = (connection is ConnectionImpl ? ((ConnectionImpl)connection).PacketQueue : ((StaticConnection)connection).staticMessages)).Count <= 0) {
                    if (iterations-- <= 0) { return; }// decrease value, if reaches 0 that means all connections have been iterated over, everything with 0 packets
                    if (++lastIteratedIndex >= l) { lastIteratedIndex = 0; }// increase and loop around the index
                    connection = connections[lastIteratedIndex];
                }

                BasePacket packet = packetQueue.Peek();// next packet in the queue

                bool sent = packet.TrySendPacket();// send it

                if (packet.sendsLeft <= 0) {// check how many sends are left
                    packet.OnSendLimitReached();// if 0, then call the event method which should remove the packet from the queue
                    l = connections.Count;// hence reassign cached length value
                }

                if (sent) { return; } else { continue; }// if packet was sent, finish logic, otherwise try another connection
            }
        }
    }

    /// <summary>
    /// Update method to be run every time the programmable block is triggered by an antenna.
    /// </summary>
    /// <param name="rawMessage">raw argument</param>
    public static void OnReceiveAntennaMessage(string rawMessage) {
        bool secure;
        string sender;
        byte channel;
        int packetUID;
        string data;
        // try to parse message header, actual data might be encrypted
        if (BasePacket.ParseReceivedMessageHeader(rawMessage, out secure, out sender, out channel, out packetUID, out data)) {
            ConnectionImpl c = (ConnectionImpl)FindConnection(sender, channel);// find the active connection
            if (c != null) {
                c.OnReceivePacket(packetUID, data, secure);// not null, let the connection object process the packet
            } else if (!secure) {// connection not found, maybe this is a request? // static packets can not be encrypted
                if (ConnectionImpl.data_requestNewConnection == data) {// non-secured connection request
                    if (onConnectionRequest != null) {// null responder, abort connection
                        if (onConnectionRequest(sender, channel)) {// ask if connection is to be opened
                            OpenNewResponseConnection(sender, channel, null);// open connection with null key -> non-encrypted
                        } else {
                            SendDataStatic(sender, channel, ConnectionImpl.data_closeConnection);
                        }
                    } else {
                        SendDataStatic(sender, channel, ConnectionImpl.data_closeConnection);
                    }
                } else if (ConnectionImpl.data_requestNewSecureConnection == data) {// secure connection request
                    if (onSecureConnectionRequest != null) {
                        byte[] key = onSecureConnectionRequest(sender, channel);
                        if (key != null) {// if key is null, abort
                            OpenNewResponseConnection(sender, channel, key);
                        } else {
                            SendDataStatic(sender, channel, ConnectionImpl.data_closeConnection);
                        }
                    } else {
                        SendDataStatic(sender, channel, ConnectionImpl.data_closeConnection);
                    }
                }
            }
        }
    }

    /// <summary>
    /// <para>Sends data to specified host over the specified channel.</para>
    /// <para>Note that the connection must be open and be approved before any data can be sent.</para>
    /// </summary>
    /// <param name="targetId">target identifier to send data to</param>
    /// <param name="channel">channel to use</param>
    /// <param name="data">data to send</param>
    /// <param name="transmissionTarget">MyTransmitTarget for antenna</param>
    /// <returns></returns>
    public static bool SendData(string targetId, byte channel, string data, MyTransmitTarget transmissionTarget = MyTransmitTarget.Default) {
        ConnectionImpl c = (ConnectionImpl)FindConnection(targetId, channel);// try find channel
        if (c != null) {// send data if not null
            c.SendData(data, transmissionTarget);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Finds an open connection.
    /// </summary>
    /// <param name="targetId">targret identifier</param>
    /// <param name="channel">channel</param>
    /// <returns>Connection object</returns>
    public static IConnection FindConnection(string targetId, byte channel) {
        ConnectionImpl c;
        for (int i = 1, l = connections.Count; i < l; i++) {
            c = (ConnectionImpl)connections[i];
            if (c.targetId == targetId && c.channel == channel) {// test connection id and channel
                return c;
            }
        }
        return null;
    }

    /// <summary>
    /// Internal method for sending static packets.
    /// </summary>
    /// <param name="targetId">target id</param>
    /// <param name="channel">channel</param>
    /// <param name="data">data to send</param>
    private static void SendDataStatic(string targetId, byte channel, string data) {
        ((StaticConnection)connections[0]).staticMessages.Enqueue(new StaticPacket(targetId, channel, data, MyTransmitTarget.Default));
    }

    /// <summary>
    /// <para>Opens new connection with the specified target id on the specified channel.</para>
    /// <para>If there already an opened connection with the specified id and channel, the found connection is returned instead.</para>
    /// <para>If target id is null, connection will not be opened.</para>
    /// </summary>
    /// <param name="targetId">target identification</param>
    /// <param name="channel">channel</param>
    /// <returns>Connection object</returns>
    public static IConnection OpenNewConnection(string targetId, byte channel, MyTransmitTarget transmissionTarget = MyTransmitTarget.Default) {
        if (targetId != null && IsInitialized) {// safeguard against non-initialized state
            for (int i = 1, l = connections.Count; i < l; i++) {// iterate starting from 1, 0 is static connection object
                ConnectionImpl c = (ConnectionImpl)connections[i];
                if (c.targetId == targetId) {// check id
                    if (c.channel == channel) {// check channel
                        return c;// connection with specified parameters is already open, return the connection object
                    }
                }
            }
            ConnectionImpl c0 = new ConnectionImpl(targetId, channel);// create new connection object
            c0.callbackPackets.Enqueue(DataPacket.InitConnectionPacket(c0, false, transmissionTarget));// enqueue initialization packet
            if (PRINTDEBUG) { Logger.Log = "onc;c=" + c0.callbackPackets.Count; }
            if (onConnectionOpen != null) {
                onConnectionOpen(c0);// call event delegate if not null
            }
            return c0;
        }
        return null;
    }

    /// <summary>
    /// <para>Opens new secure connection with the specified target id on the specified channel using the provided key.</para>
    /// <para>If there already an opened connection with the specified id and channel, the found connection is returned instead.</para>
    /// <para>If encryption hash or target id are null, connection will not be opened.</para>
    /// </summary>
    /// <param name="targetId">target identification</param>
    /// <param name="encryptionHash">hash to use in data cryptography</param>
    /// <param name="channel">channel</param>
    /// <returns>Connection object</returns>
    public static IConnection OpenNewSecureConnection(string targetId, byte[] encryptionHash, byte channel = 1, MyTransmitTarget transmissionTarget = MyTransmitTarget.Default) {
        if (targetId != null && encryptionHash != null && IsInitialized) {
            for (int i = 1, l = connections.Count; i < l; i++) {
                ConnectionImpl c = (ConnectionImpl)connections[i];
                if (c.targetId == targetId) {
                    if (c.channel == channel) {
                        return c;
                    }
                }
            }
            ConnectionImpl c0 = new ConnectionImpl(targetId, channel, encryptionHash);
            c0.callbackPackets.Enqueue(DataPacket.InitConnectionPacket(c0, true, transmissionTarget));
            if (onConnectionOpen != null) {
                onConnectionOpen(c0);
            }
            return c0;
        }
        return null;
    }

    /// <summary>
    /// Opens a new connection as a response to a received request.
    /// </summary>
    /// <param name="targetId">id of the connection</param>
    /// <param name="channel">channel</param>
    /// <param name="hash">encryption has if any</param>
    /// <returns></returns>
    private static ConnectionImpl OpenNewResponseConnection(string targetId, byte channel, byte[] hash) {
        if (IsInitialized) {// make sure protocol is initialized
            for (int i = 1, l = connections.Count; i < l; i++) {
                ConnectionImpl c = (ConnectionImpl)connections[i];
                if (c.targetId == targetId) {
                    if (c.channel == channel) {
                        return null;
                    }
                }
            }
            if (PRINTDEBUG) { Logger.Log = "orc;t=" + targetId + ";c=" + channel + ";k=" + (hash == null ? "null" : hash.ToString()); }
            ConnectionImpl c0 = (hash == null ? new ConnectionImpl(targetId, channel) : new ConnectionImpl(targetId, channel, hash));
            c0.callbackPackets.Enqueue(DataPacket.NewAcceptResponsePacket(c0, MyTransmitTarget.Default));
            if (onConnectionOpen != null) {
                onConnectionOpen(c0);
            }
            return c0;
        }
        return null;
    }

    /// <summary>Static search argument for <see cref="PACKETSEARCH_PacketUIDMatches(BasePacket)"/> and <see cref="PACKETSEARCH_TargetAndUIDMatch(BasePacket)"/></summary>
    private static int PACKETSEARCH_searchedPacketUID;
    private static bool PACKETSEARCH_PacketUIDMatches(BasePacket packet) {// used to search packets only by their uid
        return packet.packetUID == PACKETSEARCH_searchedPacketUID;
    }
    private static ConnectionImpl PACKETSEARCH_target;// target argument to search for
    private static bool PACKETSEARCH_TargetAndUIDMatch(BasePacket packet) {// used to search packets by their uid and target
        if (packet.packetUID != PACKETSEARCH_searchedPacketUID) { return false; }
        if (packet is DataPacket) {
            return ((DataPacket)packet).target == PACKETSEARCH_target;
        } else if (packet is CloseConnectionPacket) {
            return ((CloseConnectionPacket)packet).target == PACKETSEARCH_target;
        } else if (packet is StaticPacket) {
            return ((StaticPacket)packet).targetId == PACKETSEARCH_target.targetId && ((StaticPacket)packet).channel == PACKETSEARCH_target.channel;
        }
        return false;
    }

    /// <summary>
    /// General interface for connection storage
    /// </summary>
    private interface IBaseConnection { void Close(); }

    /// <summary>
    /// Connection interface
    /// </summary>
    public interface IConnection {
        /// <summary>
        /// Target identification
        /// </summary>
        string TargetID { get; }

        /// <summary>
        /// Connection Channel
        /// </summary>
        byte Channel { get; }

        /// <summary>
        /// Connection data encryption status
        /// </summary>
        bool IsSecure { get; }

        /// <summary>
        /// Connection state
        /// </summary>
        bool IsAlive { get; }

        /// <summary>
        /// Connection state
        /// </summary>
        bool IsClosed { get; }

        /// <summary>
        /// Queues up data to be sent.
        /// </summary>
        /// <param name="data">data to send</param>
        /// <param name="transmissionTarget">transmission target for antenna</param>
        /// <returns>True if the data was queued up, false otherwise</returns>
        bool SendData(string data, MyTransmitTarget transmissionTarget = MyTransmitTarget.Default);

        /// <summary>
        /// Closes connection down.
        /// </summary>
        void Close();
    }

    /// <summary>
    /// Used for storing static packets.
    /// </summary>
    private sealed class StaticConnection : IBaseConnection {
        public QueueList<BasePacket> staticMessages = new QueueList<BasePacket>();// static list

        public void Close() {
            if (staticMessages != null) {
                staticMessages.Clear();
                staticMessages = null;
            }
        }

        public override string ToString() {
            if (PRINTDEBUG) {
                string packets = "";
                foreach (var c in staticMessages) {
                    packets = packets + "i:" + c.packetUID + "m=" + c.cachedMessage + ",";
                }
                return "SMC (" + staticMessages.Count + ") [" + packets + "]";
            } else { return base.ToString(); }
        }
    }

    /// <summary>
    /// Internal connection object
    /// </summary>
    private sealed class ConnectionImpl : IBaseConnection, IConnection {
        public const string data_requestNewConnection = "connectionRequest";
        public const string data_requestNewSecureConnection = "connectionSecureRequest";
        public const int packetUID_connectionRequest = -2;
        public const string data_acceptConnectionResponse = "connectionAccept";
        public const int packetUID_acceptConnectionResponse = -3;
        public const string data_connectionSuccessfulHandshake = "connectionHandshake";
        public const int packetUID_connectionSuccessfulHandshake = -4;// used locally only
        public const string data_closeConnection = "closeConnection";

        public const int packetUID_callback = maxPacketNumber + 100;// callback packet uid

        public const string data_packetReceivedCallback = "packetReceived:";// callback packet data header
        public static readonly int receivedCallbackSubstringParsePos = data_packetReceivedCallback.Length;// parsing length

        public const byte approve_approved = 246;// state: connection approved, can transmit user data
        public const byte approve_pending = 154;// state: pending, initial state, connection is initializing
        public const byte approve_closing = 138;// state: closing connection is about to close, no data can be sent/received

        private const int maxPacketNumber = 8000;// max packet number, when reaches this value loops back to 0
        private const int lastAccountedPacketCount = 1000;// number of packets that can be determined to have been recently received

        public string targetId;// id of this connection
        public byte channel;

        public string TargetID { get { return targetId; } }
        public byte Channel { get { return channel; } }
        public bool IsSecure { get { return hashKey != null; } }
        public bool IsAlive { get { return standardPackets != null; } }
        public bool IsClosed { get { return callbacksInARow < -100; } }

        public byte[] hashKey = null;// hashing key for secure connection

        private int callbacksInARow = 0;// used to determine whether to send queued callback packet or next data packet

        public QueueList<BasePacket> standardPackets = new QueueList<BasePacket>();// user data packets
        public QueueList<BasePacket> callbackPackets = new QueueList<BasePacket>();// callback packets
        public QueueList<BasePacket> PacketQueue {
            get {
                if (callbacksInARow < 5 && callbackPackets.Count > 0) { callbacksInARow++; return callbackPackets; } else { callbacksInARow = 0; return standardPackets; }
            }
        }

        public byte approved = approve_pending;// connection state

        private int m_lastReceivedPacketUID = 0;// used to determine if a newly received packet is new
        private int m_nextSentPacketUID = 0;// used to number outgoing packets
        public int NextSentPacketUID {// increments and loops if needed next packet number
            get {
                return ++m_nextSentPacketUID > maxPacketNumber ? m_nextSentPacketUID = 0 : m_nextSentPacketUID;
                //if (++m_nextSentPacketUID > maxPacketNumber) { m_nextSentPacketUID = 0; } return m_nextSentPacketUID;
            }
        }

        /// <summary>
        /// Unencrypted connection constructor.
        /// </summary>
        public ConnectionImpl(string targetId, byte channel) {
            connections.Add(this);// add connection to the connection list
            this.targetId = targetId;
            this.channel = channel;
        }

        /// <summary>
        /// Encrypted connection constructor.
        /// </summary>
        public ConnectionImpl(string targetId, byte channel, byte[] encryptionHash) : this(targetId, channel) {
            hashKey = encryptionHash;
            /*int hash = 0;
            for (int i = 0, j = 0, l = encryptionHash.Length; i < l; i++, j = ((8 * i) % 25)) {
                hash = ((((hash >> j) + i) ^ encryptionHash[i]) << j);
            }
            secureByteGenerator = new Random(hash);*/
        }

        public bool SendData(string data, MyTransmitTarget transmissionTarget = MyTransmitTarget.Default) {
            if (data != null && standardPackets != null && approved == approve_approved) {// ensure that the connection is ready and data is not null
                standardPackets.Enqueue(DataPacket.NewDataPacket(this, data, transmissionTarget));
                return true;
            }
            return false;
        }

        public void Close() {
            if (connections.Remove(this)) {//remove from list
                if (approved != approve_closing) {// check whether this method has already been called
                    approved = approve_closing;// set closing flag and queue up close message to inform the target
                    ((StaticConnection)connections[0]).staticMessages.Enqueue(new CloseConnectionPacket(this, MyTransmitTarget.Default));
                }
            }
            hashKey = null;
            if (standardPackets != null) {
                standardPackets.Clear();
                standardPackets = null;
            }
            if (callbackPackets != null) {
                callbackPackets.Clear();
                callbackPackets = null;
            }
        }

        /// <summary>
        /// Run when the close packet has ben dispatched, and the connection is completely closed on this end.
        /// </summary>
        public void CompleteClose() {
            callbacksInARow = -1000000000;// internal flag
            if (onConnectionClose != null) {
                onConnectionClose(this);
            }
        }

        public void OnReceivePacket(int packetUID, string data, bool secure) {
            if (PRINTDEBUG) { Logger.Log = (IsSecure != secure ? "x_" : "") + "r:n=" + (packetUID >= 0 ? (IsNewPacket(packetUID) ? "y" : "n") : "-") + ";i=" + packetUID + ";s=" + (secure ? "y" : "n") + "; d=" + data; }
            if (IsSecure != secure) { return; }
            if (packetUID < 0) {// packet uids less than 0 indicate special system packets
                if (secure) { if (!TryDecryptPacketData(ref data)) { return; } }// if encrypted, try to decrypt, return if failed to do so
                if (data == data_acceptConnectionResponse) {// initialization step after connection request
                    if (approved == approve_pending) { approved = approve_approved; }// set the flag if it was pending
                    PACKETSEARCH_searchedPacketUID = packetUID_connectionRequest;// search for requestConnection the packet
                    PACKETSEARCH_target = this;
                    BasePacket p = callbackPackets.RemoveFirstMatching(PACKETSEARCH_TargetAndUIDMatch);// remove the request packet
                    if (p != null) { p.ClearPacketData(); }
                    PACKETSEARCH_target = null;
                    callbackPackets.Enqueue(DataPacket.NewHandshakePacket(this, MyTransmitTarget.Default));// send handshake packet to finish initialization
                } else if (data == data_connectionSuccessfulHandshake) {// initialization step after sending request accept
                    if (approved == approve_pending) { approved = approve_approved; }
                    PACKETSEARCH_searchedPacketUID = packetUID_acceptConnectionResponse;// search of acceptConnection packet
                    PACKETSEARCH_target = this;
                    BasePacket p = callbackPackets.RemoveFirstMatching(PACKETSEARCH_TargetAndUIDMatch);// remove the accept packet
                    if (p != null) { p.ClearPacketData(); }
                    PACKETSEARCH_target = null;
                } else if (data == data_closeConnection) {// close connection packet
                    approved = approve_closing;// set the flag beforehand to avoid close packet beeing sent by thos connection
                    Close();
                    CompleteClose();
                }
            } else if (packetUID == packetUID_callback) {// callback packet
                if (secure) { if (!TryDecryptPacketData(ref data)) { return; } }
                if (data.StartsWith(data_packetReceivedCallback)) {// packetReceived callback
                    if (int.TryParse(data.Substring(receivedCallbackSubstringParsePos), out PACKETSEARCH_searchedPacketUID)) {// try parse packet uid
                        BasePacket p = standardPackets.RemoveFirstMatching(PACKETSEARCH_PacketUIDMatches);// try remove from standard packet queue
                        if (p == null) {// if not found, try removing from callback packet queue
                            PACKETSEARCH_target = this;
                            p = callbackPackets.RemoveFirstMatching(PACKETSEARCH_TargetAndUIDMatch);
                            PACKETSEARCH_target = null;
                        }
                        if (p != null) { p.ClearPacketData(); }
                    }
                }
            } else if (IsNewPacket(packetUID)) {
                if (secure) { if (TryDecryptPacketData(ref data)) { } else { return; } }
                callbackPackets.Enqueue(DataPacket.NewCallbackPacket(this, packetUID, MyTransmitTarget.Default));// packet received, queue callback packet
                m_lastReceivedPacketUID = packetUID;
                if (approved == approve_approved) { onDataReceive(this, data); }
            } else { callbackPackets.Enqueue(DataPacket.NewCallbackPacket(this, packetUID, MyTransmitTarget.Default)); }// old packet received for some reason, queue a callback packet for it
        }

        /// <summary>
        /// Decrypts data.
        /// </summary>
        /// <param name="data">data to decrypt</param>
        /// <returns>True if data could be decrypted, false otherwise</returns>
        private bool TryDecryptPacketData(ref string data) {
            if (IsSecure) {
                if (!BasePacket.Decrypt(ref data, hashKey)) { return false; }
            } else { return false; }
            if (PRINTDEBUG) { Logger.Log = "decrypted=" + data; }
            return true;
        }

        /// <summary>
        /// Checks whether the specified packet uid is considered to be not recently received
        /// </summary>
        /// <param name="packetUID">packet uid to test</param>
        /// <returns>True if the packet is new, false if the packet has already been received</returns>
        public bool IsNewPacket(int packetUID) {
            int min = m_lastReceivedPacketUID - lastAccountedPacketCount;
            if (min < 0) {
                return packetUID < min || packetUID > m_lastReceivedPacketUID;// check outward unbound
            } else {
                return packetUID > m_lastReceivedPacketUID && packetUID < min;// check inward bounded
            }
        }

        public override string ToString() {
            return "sender id: " + targetId + "\nchannel: " + channel + "\nsecure: " + IsSecure;
        }
    }

    /// <summary>
    /// Base packet class
    /// </summary>
    private abstract class BasePacket {
        public MyTransmitTarget transmissionTarget;
        public string cachedMessage;
        public int sendsLeft = maxSendCount;
        public int packetUID = -1;

        private string debug_data;

        protected BasePacket(int packetUID, MyTransmitTarget transmissionTarget) {
            this.packetUID = packetUID;
            this.transmissionTarget = transmissionTarget;
        }

        /// <summary>
        /// Constructs packet data in format: [encryption status]:target id|sender id|channel id|packet id>user data
        /// </summary>
        protected string ConstructTransmissionData(ConnectionImpl connection, string targetId, byte channel, long packetUID, string data) {
            if (PRINTDEBUG) { debug_data = data; }
            bool encrypted = connection != null && connection.IsSecure;
            if (encrypted) { Encrypt(ref data, connection.hashKey); }
            return (encrypted ? "[encrypted]:" : "[unencrypted]:") + targetId + "|" + hostId + "|" + channel + "|" + packetUID.ToString() + ">" + data;
        }

        /// <summary>
        /// Tries to send this packet, decrementing sendsLeft.
        /// </summary>
        /// <returns>True if the packet was sent, false otherwise</returns>
        public bool TrySendPacket() {
            if (PRINTDEBUG) { Logger.Log = "s:i=" + packetUID + "; d=" + debug_data; }
            bool retVal = antenna.TransmitMessage(cachedMessage, transmissionTarget);
            if (retVal) { sendsLeft--; }// decrease count if sent successfully
            return retVal;
        }

        /// <summary>
        /// Encrypts specified data with the specified key.
        /// </summary>
        public static void Encrypt(ref string data, byte[] key) {
            if (PRINTDEBUG) { Logger.LogArray("key: ", key); Logger.Log = "unencrypted:" + data; }
            data = EncryptData(data, key);
            if (PRINTDEBUG) { Logger.Log = "encrypted:" + data; }
        }

        /// <summary>
        /// Tries to decrypt specified data with the specified key.
        /// </summary>
        public static bool Decrypt(ref string data, byte[] key) {
            if (PRINTDEBUG) { Logger.LogArray("decrypting key: ", key); }
            if (TryDecryptData(ref data, key)) {
                if (PRINTDEBUG) { Logger.Log = "decrypted:" + data; }
                return true;
            } else if (PRINTDEBUG) { Logger.Log = "not decrypted"; }
            return false;
        }

        /// <summary>
        /// Tries to parse received message.
        /// </summary>
        /// <param name="rawMessage">argument passed to the command block by antenna</param>
        /// <param name="secure">whether the packet is labelled encrypted</param>
        /// <param name="sender">sender id</param>
        /// <param name="channel">channel used</param>
        /// <param name="packetUID">packet uid</param>
        /// <param name="data">data, may be encrypted</param>
        /// <returns>True if header was parsed, false otherwise</returns>
        public static bool ParseReceivedMessageHeader(string rawMessage, out bool secure, out string sender, out byte channel, out int packetUID, out string data) {
            Logger.Log = "rec:" + rawMessage;
            int a = rawMessage.IndexOf(':');
            if (a > 0) {
                string b = rawMessage.Substring(0, a + 1);
                string mainMessage;
                if (b == "[encrypted]:") {
                    secure = true;
                } else if (b == "[unencrypted]:") {// if it isnt of of the 2 headers, then its wrong syntax, deny packet
                    secure = false;
                } else {// return if label is not standard
                    secure = false;
                    sender = null;
                    channel = 0;
                    packetUID = 0;
                    data = null;
                    return false;
                }
                mainMessage = rawMessage.Substring(a + 1);// header starts here
                a = mainMessage.IndexOf('|');
                if (a > 0) {
                    string tmpString = mainMessage.Substring(0, a);// receiver id
                    if (tmpString == hostId) {// check if the message is for this grid
                        int c = mainMessage.IndexOf('|', ++a);
                        if (c > 0) {
                            sender = mainMessage.Substring(a, c - a);
                            a = mainMessage.IndexOf('|', ++c);
                            if (a > 0) {
                                tmpString = mainMessage.Substring(c, a - c);// channel
                                if (byte.TryParse(tmpString, out channel)) {
                                    c = mainMessage.IndexOf('>', ++a);
                                    if (c > 0) {
                                        tmpString = mainMessage.Substring(a, c - a);// packet uid
                                        if (int.TryParse(tmpString, out packetUID)) {
                                            data = mainMessage.Substring(c + 1);// data, may be encrypted
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            secure = false;
            sender = null;
            channel = 0;
            packetUID = 0;
            data = null;
            return false;
        }

        /// <summary>
        /// Called from the update method when the packet has reached it send limit
        /// </summary>
        public virtual void OnSendLimitReached() { }

        /// <summary>
        /// Null out references to make garbage collection easier
        /// </summary>
        public virtual void ClearPacketData() {
            cachedMessage = null;
        }
    }

    /// <summary>
    /// Data packet class, used for most system packets and user data transportation
    /// </summary>
    private class DataPacket : BasePacket {
        public ConnectionImpl target;// connection to which send data to

        public static DataPacket NewHandshakePacket(ConnectionImpl target, MyTransmitTarget transmissionTarget) {
            DataPacket p = new DataPacket(target, ConnectionImpl.data_connectionSuccessfulHandshake, transmissionTarget, ConnectionImpl.packetUID_connectionSuccessfulHandshake);
            p.sendsLeft = 1;
            return p;
        }

        public static DataPacket NewAcceptResponsePacket(ConnectionImpl target, MyTransmitTarget transmissionTarget) {
            return new DataPacket(target, ConnectionImpl.data_acceptConnectionResponse, transmissionTarget, ConnectionImpl.packetUID_acceptConnectionResponse);
        }

        private DataPacket(ConnectionImpl target, string data, MyTransmitTarget transmissionTarget, int packetUID) : base(packetUID, transmissionTarget) {
            this.target = target;
            cachedMessage = ConstructTransmissionData(target, target.targetId, target.channel, packetUID, data);
        }

        public static DataPacket InitConnectionPacket(ConnectionImpl target, bool secure, MyTransmitTarget transmissionTarget) {
            return new DataPacket(target, secure, transmissionTarget);
        }

        private DataPacket(ConnectionImpl target, bool secure, MyTransmitTarget transmissionTarget) : base(ConnectionImpl.packetUID_connectionRequest, transmissionTarget) {
            this.target = target;
            cachedMessage = ConstructTransmissionData(null, target.targetId, target.channel, ConnectionImpl.packetUID_connectionRequest, secure ? ConnectionImpl.data_requestNewSecureConnection : ConnectionImpl.data_requestNewConnection);
        }

        public static DataPacket NewCallbackPacket(ConnectionImpl target, int receivedPacketUID, MyTransmitTarget transmissionTarget) { return new DataPacket(target, receivedPacketUID, transmissionTarget); }

        private DataPacket(ConnectionImpl target, int receivedPacketUID, MyTransmitTarget transmissionTarget) : base(ConnectionImpl.packetUID_callback, transmissionTarget) {
            this.target = target;
            sendsLeft = 1;
            cachedMessage = ConstructTransmissionData(target, target.targetId, target.channel, ConnectionImpl.packetUID_callback,
            ConnectionImpl.data_packetReceivedCallback + receivedPacketUID.ToString());
        }

        public static DataPacket NewDataPacket(ConnectionImpl target, string data, MyTransmitTarget transmissionTarget) { return new DataPacket(target, data, transmissionTarget); }
        private DataPacket(ConnectionImpl target, string data, MyTransmitTarget transmissionTarget) : base(target.NextSentPacketUID, transmissionTarget) {
            this.target = target;
            cachedMessage = ConstructTransmissionData(target, target.targetId, target.channel, packetUID, data);
        }

        public override void OnSendLimitReached() {// callback and handshake packets do not cause the connection to close
            if (target != null && packetUID != ConnectionImpl.packetUID_callback && packetUID != ConnectionImpl.packetUID_connectionSuccessfulHandshake) {
                target.Close();
            }
            ClearPacketData();
        }

        public override void ClearPacketData() {
            if (target != null) {// remove this packet from possible queues
                if (target.standardPackets != null) {
                    target.standardPackets.Remove(this);
                }
                if (target.callbackPackets != null) {
                    target.callbackPackets.Remove(this);
                }
                target = null;
            }
            base.ClearPacketData();
        }
    }

    /// <summary>
    /// CloseConnection packet, used for closing connections
    /// </summary>
    private class CloseConnectionPacket : BasePacket {
        public ConnectionImpl target;
        public CloseConnectionPacket(ConnectionImpl target, MyTransmitTarget transmissionTarget) : base(-1, transmissionTarget) {
            this.target = target;
            sendsLeft = 1;
            cachedMessage = ConstructTransmissionData(target, target.targetId, target.channel, -1, ConnectionImpl.data_closeConnection);
        }

        public override void OnSendLimitReached() {
            if (target != null) {
                target.approved = ConnectionImpl.approve_closing;// make sure Close method will not queue another close packet
                target.Close();
                target.CompleteClose();
                target = null;
            }
            ((StaticConnection)connections[0]).staticMessages.Remove(this);// can only be in static queue
            ClearPacketData();
        }
    }

    /// <summary>
    /// Static packet that defines to Connection object, only abstract details.
    /// </summary>
    private class StaticPacket : BasePacket {
        public string targetId;
        public byte channel;

        public StaticPacket(string targetId, byte channel, string data, MyTransmitTarget transmissionTarget) : base(-1, transmissionTarget) {
            this.targetId = targetId;
            this.channel = channel;
            sendsLeft = 1;
            cachedMessage = ConstructTransmissionData(null, targetId, channel, -1, data);
        }

        public override void OnSendLimitReached() { ((StaticConnection)connections[0]).staticMessages.Remove(this); }
    }

    /// <summary>
    /// LinkedList derived queue, used for packet storage. LinkedList is derived as it allows element iteration and allows faster Enqueue and Dequeue operations.
    /// </summary>
    /// <typeparam name="T">QueueList Type</typeparam>
    private class QueueList<T> : LinkedList<T> {
        public void Enqueue(T element) {
            AddFirst(element);
        }
        public T Dequeue() {
            T val = Last.Value;
            RemoveLast();
            return val;
        }
        public T Peek() {
            return Last.Value;
        }
        public LinkedListNode<T> FindMatching(Func<T, bool> selector) {// iteration take from LinkedList source
            LinkedListNode<T> node = First;
            if (node != null) {
                if (selector != null) {
                    do {
                        if (selector(node.Value)) { return node; }
                        node = node.Next;
                    } while (node != First);
                }
            }
            return null;
        }
        public T RemoveFirstMatching(Func<T, bool> selector) {
            LinkedListNode<T> n = FindMatching(selector);
            T val = default(T);
            if (n != null) { val = n.Value; Remove(n); }
            return val;
        }
    }

    private static readonly Encoding encoding = Encoding.ASCII;// default encoding

    /// <summary>
    /// Encrypts data string into base64.
    /// </summary>
    /// <param name="data">string to encrypt</param>
    /// <param name="key">encryption key</param>
    /// <returns>Base64 encrypted string</returns>
    public static string EncryptData(string data, byte[] key) {
        byte[] data0 = encoding.GetBytes(data);// decode string to bytes
        for (int i = 0, j = 0, l = data0.Length, l1 = key.Length; i < l; i++, j = ++j >= l1 ? (j = 0) : j) {
            data0[i] = (byte)(data0[i] ^ (key[j]));// xor data bytes and the key
        }
        return Convert.ToBase64String(data0);// convert to base64 form
    }

    /// <summary>
    /// Decrypts a base64 string.
    /// </summary>
    /// <param name="data">base64 string to decrypt, will be decrypted to original, or null if the supplied string was not in base64 format<</param>
    /// <param name="key">encryption key</param>
    /// <returns>True if decryption suceeded, false otherwise</returns>
    public static bool TryDecryptData(ref string data, byte[] key) {
        if (IsBase64String(data)) {// guard against Convert.FromBase64String exceptions by checking the string beforehand
            byte[] input = Convert.FromBase64String(data);// convert base64 into encrypted bytes
            for (int i = 0, j = 0, l = input.Length, l1 = key.Length; i < l; i++, j = ++j >= l1 ? (j = 0) : j) {
                input[i] = (byte)(input[i] ^ (key[j]));// xor data and the key
            }
            data = encoding.GetString(input);// encode back to the original encoding
            return true;
        }
        return false;
    }

    /// <summary>
    /// Checks whether the specified string is a valid Base64 encoded string.
    /// </summary>
    /// <param name="s">string to check</param>
    /// <returns>True if the string is valid, false otherwise</returns>
    public static bool IsBase64String(string s) {
        int l = s.Length;
        if (l == 0) { return true; }// Base64 allows 0 length
        if (l % 4 != 0) { return false; }// Only allow multiple of 4 length if not 0
        l = s.IndexOf('=');
        if (l > 0) {
            if (l < s.Length - 2) { return false; }// only last 2 characters may be '='
        } else { l = s.Length; }
        char c;
        for (int i = 0; i < l; i++) {
            c = s[i];
            // permit only alpha-numeric characters, '+' and '/'
            if ((c < 48 || c > 57) && (c < 65 || c > 90) && (c < 97 || c > 122) && (c != '+' && c != '/')) { return false; }
        }
        return true;
    }

    public static class Logger {
        private static readonly StringBuilder m_message = new StringBuilder();
        public static string Message {
            get { return m_message.ToString(); }
            set { m_message.Clear(); if (value != null) { m_message.Append(value.ToString()).Append('\n'); } }
        }
        public static object Log { set { m_message.Append(value.ToString()).Append('\n'); } }
        public static void LogArray<T>(string prefix, T[] array) {
            if (prefix != null) { m_message.Append(prefix); }
            m_message.Append(array.ToString()).Append('{');
            int l = array.Length;
            if (l > 0) {
                m_message.Append(array[0].ToString());
                for (int i = 1; i < l; i++) {
                    m_message.Append(',').Append(' ').Append(array[i].ToString());
                }
            }
            m_message.Append(']').Append('\n');
        }
    }
}
