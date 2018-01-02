# Space Engineers Radio Transmission Protocol
Space Engineers data transmission protocol based around reliably sending and receiving data across different grids using radio antennas.

Steam Workshop: http://steamcommunity.com/sharedfiles/filedetails/?id=1254505101
## Current Features
1. High level API - requires minimal time to write code to use the protocol
2. Reliable packet delivery - packets are sent until callback response is received
3. Inbuilt symmetric encryption - connection can secured by providing a key to encrypt transmitted data

## Overview
### 1 Block Setup
1. Programmable Block
2. Antenna set to trigger the PB
### 2 API Usage
#### 2.1 Protocol Initialization
Before opening any connections the protocol must be initialized with `TransmissionProtocol.Init` method.

Initialization Arguments:
1. [`IMyRadioAntenna` antenna] Antenna to use in transmission
2. [`string` hostId] Identifier for this protocol instance, alternative of an IP
3. [`Action<IConnection, string>` onDataReceive] Called when a request to open an unencrypted connection is received
   * (param) [`IConnection`] connection object
   * (param) [`string`] data received
4. [`Func<string, byte, bool>` onConnectionRequest] Called when a request to open an unencrypted connection is received
   * (param) [`string`] sender identifier, alternative of an IP
   * (param) [`byte`] channel, alternative of ports
   * (return) [`bool`] `true` to accept connection, `false` to decline
5. [`Func<string, byte, byte[]>` onSecureConnectionRequest] Called when a request to open an encrypted connection is received
   * (param) [`string`] sender identifier, alternative of an IP
   * (param) [`byte`] channel, alternative of ports
   * (return) [`byte[]`] key to use in the encyption, or `null` to decline
6. [`Action<IConnection>` onConnectionOpen] Called when a new connection is opened
   * (param) [`IConnection`] connection that has been opened
7. [`Action<IConnection>` onConnectionClose] Called when a connection is closed
   * (param) [`IConnection`] connection that has been closed
8. [`int` maxPacketResendCount = 10] Maximal amount of times to send data packets before closing down related connection

#### 2.2 Protocol Update
The script is designed to be run frequently (`Runtime.UpdateFrequency = UpdateFrequency.Update1/Update10/Update100`).

On each PB invocation the script should:
1. Test whether UpdateType is Antenna, in which case `TransmissionProtocol.OnReceiveAntennaMessage` should be invoked, passing the argument from the `Main` method
   >Processes the message and invokes `onDataReceive` delegate
2. Invoke `TransmissionProtocol.UpdateLogic`
   >Attempts to send exactly one packet from the currently queued packets
#### 2.3 Opening Connections
Connection can be opened with `OpenNewConnection(string, byte)` or `OpenNewSecureConnection(string, byte, byte[])`. Both methods require target identifier (`string`) and channel (`byte`) to operate. Secure connection also requres a `byte array` as a key, which can be obtained from a `string` by calling `StringToHash(string)` which will convert the specified string to bytes.

Note that after opening the connection there will be a few update cycles (usually 4 ticks) when the connection will not be able to send data, and any data queued will be discarded. To avoid this, only cache the connection object from `onConnectionOpen` delegate.
#### 2.4 Accepting Connections
When a connection is opened, the target will have the corresponding connection request delegate invoked (`onConnectionRequest` for unencrypted connection and `onSecureConnectionRequest` for encrypted). In the case of the encrypted connection, the delegate must return the same key that was used to open the connection, otherwise the connection will be terminated.
#### 2.5 Data Transmission
It is recommended to run `UpdateLogic` more frequently than sending data, as that will ensure no message build up in the system, which could result in connection closing down.

To send data through an open connection invoke `IConnection.SendData`, passing a `string` as an argument. If the method returned `true`, the data was queued up and will be sent in the next few update cycles. However, a `false` return value can indicate that the connection is not yet ready for data transmission, or is closed. These conection states can be tested with `IsReady` and `IsClosed` properties of `IConnection` object.

Once the data is sent, it will be received by the target and `onDataReceive` delegate will be invoked on the target's PB.
### 3 Which code do I use?
1. TransmissionProtocol.cs
   >Source code and the api reference that should be used if developing a script in Visual Studio
2. TransmissionProtocol_PB_API.cs
   >Compressed and somewhat obfuscated code with all internal member and variable names reduced to one-letter to save space in the programming block
3. PB_Test.cs
   >A test script that is used to test whether the the protocol is working as intended
## Notes
1. At the moment the script has not been widely tested, and as such may contain bugs that will cause connections to close at some points or script termination.
2. Any bug reports are welcome to be submitted (include exception from script and brief summary of actions that lead to the script termination).
3. Current encryption is not supposed to be something unbreakable, simply makes data reading harder without the key.

## Planned Features
1. Increase bandwidth by supporting more than one antenna, as each antenna can only transmit once per tick.
2. Rewrite the code to be more compact and clean (this is the first attempt).
