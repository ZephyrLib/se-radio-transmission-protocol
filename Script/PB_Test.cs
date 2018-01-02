/// Test PB Script for TransmissionProtocol
/// Specify a unique 'Name' for each PB
/// Command Format: [command]:[arg 0]:[arg 1]:...
///
/// Commands:
///           connect:[target Name]:[channel]:[encrypted ? (true/false)]// initiates a new connection
///           send:[text data]// sends data over an open connection if there is such
///           disonnect// disconnects an open connection if there is such

public const string Name = "HostA";// unique name for this protocol instance

const string def_block_antenna = "Antenna";// name of the antenna to use
const string def_block_lcdPanel = "LCD";// panel to output received data to

IMyRadioAntenna antenna;
IMyTextPanel lcdPanel;

string messages = "";

Program() {
    List<IMyTerminalBlock> blocks = new List<IMyTerminalBlock>();
    GridTerminalSystem.SearchBlocksOfName(def_block_antenna, blocks);
    antenna = (IMyRadioAntenna)blocks[0];
    GridTerminalSystem.SearchBlocksOfName(def_block_lcdPanel, blocks);
    lcdPanel = (IMyTextPanel)blocks[0];

    antenna.AttachedProgrammableBlock = Me.EntityId;

    TransmissionProtocol.Init(antenna, Name, OnDataReceive, OnConnectionRequest, OnSecureConnectionRequest, OnConnectionOpen, OnConnectionClose);

    Runtime.UpdateFrequency = UpdateFrequency.Update10;
}

TransmissionProtocol.IConnection connection;

void Main(string arg, UpdateType updateType) {
    if (updateType == UpdateType.Antenna) {// process received transmission
        TransmissionProtocol.OnReceiveAntennaMessage(arg);
    } else if (updateType == UpdateType.Terminal) {// process user command
        string[] args = arg.Split(':');
        string cmd = args[0];
        if (cmd == "connect") {
            if (connection == null) {
                if (arg.Length > 3) {
                    string targetId = args[1];
                    byte channel;
                    if (byte.TryParse(args[2], out channel)) {
                        bool secure = args[3].ToLower() == "true";
                        TransmissionProtocol.IConnection c;
                        if (secure) {
                            c = TransmissionProtocol.OpenNewSecureConnection(targetId, channel, TransmissionProtocol.StringToHash("1234"));
                        } else {
                            c = TransmissionProtocol.OpenNewConnection(targetId, channel);
                        }
                        connection = c;
                    }
                }
            }
        } else if (cmd == "disconnect") {
            if (connection != null) {
                connection.Close();
                connection = null;
            }
        } else if (cmd == "send") {
            if (connection != null) {
                if (arg.Length > 1) {
                    connection.SendData(args[1]);
                }
            }
        }
    }
    TransmissionProtocol.UpdateLogic();
    if (lcdPanel != null) { lcdPanel.WritePublicText((connection == null ? "disconnected" : connection.ToString()) + "\n" + messages); }
}

void OnDataReceive(TransmissionProtocol.IConnection connection, string data) { messages += data + "\n"; }

bool OnConnectionRequest(string hostId, byte channel) { return connection == null; }

byte[] OnSecureConnectionRequest(string histId, byte channel) { return connection == null ? TransmissionProtocol.StringToHash("1234") : null; }

void OnConnectionOpen(TransmissionProtocol.IConnection connection) { if (this.connection != null) { this.connection.Close(); } this.connection = connection; }

void OnConnectionClose(TransmissionProtocol.IConnection connection) { if (connection == this.connection) { this.connection = null; } }


/// Compressed TransmissionProtocol class, PB ready
static class TransmissionProtocol{static IMyRadioAntenna n;static string q;static Action<H,string>r;static Func<string,byte,bool>u;static Func<string,byte,byte[]>v;static Action<IConnection>w;static Action<IConnection>L;static int
N;static List<F>R;static int T=0;static bool IsInitialized{get{return n!=null&&q!=null;}}public static byte[]StringToHash(string key){byte[]a=Encoding.Unicode.GetBytes(key);int l=a.Length;byte b=a[l-1];for(int i=0,j=1;i<l;i++,
j++){a[i]=(byte)(a[i]<<(((i>>0)&0xff)^(j<l?a[j]:b)));}return a;}public static bool Init(IMyRadioAntenna antenna,string hostId,Action<IConnection,string> onDataReceive,Func<string,byte,bool>onConnectionRequest,Func<string,byte,
byte[]>onSecureConnectionRequest,Action<IConnection>onConnectionOpen,Action<IConnection>onConnectionClose,int maxPacketResendCount=10){Shutdown();if(antenna==null||hostId==null){return false;}n=antenna;q=hostId;r=onDataReceive;u
=onConnectionRequest;v=onSecureConnectionRequest;w=onConnectionOpen;L=onConnectionClose;N=maxPacketResendCount>0?maxPacketResendCount:1;R=new List<F>();R.Add(new E());return true;}public static void Shutdown(){n=null;q=null;r=
null;u=null;v=null;w=null;L=null;if(R!=null){for(int i=1,l=R.Count;i<l;i++){R[i].Close();}Q<G>a=((E)R[0])._k;if(a!=null){while(a.Count>0){a.B().p();}}R.Clear();R=null;}T=0;}public static void UpdateLogic(){int l=R.Count;if(l>0)
{int m=l;while(true){if(++T>=l){T=0;}F c=R[T];Q<G>packetQueue;while((packetQueue=(c is H?((H)c).y:((E)c)._k)).Count<=0){if(m--<=0){return;}if(++T>=l){T=0;}c=R[T];}G p=packetQueue.C();bool s=p.f();if(p.c<=0){p.o();l=R.Count;}if(s)
{return;}else{continue;}}}}public static void OnReceiveAntennaMessage(string rawMessage){bool s;string t;byte d;int p;string e;if(G.h(rawMessage,out s,out t,out d,out p,out e)){H c=(H)FindConnection(t,d);if(c!=null){c.J(p,e,s);}
else if(!s){if(H.a==e){if(u!=null){if(u(t,d)){OpenNewResponseConnection(t,d,null);}else{S(t,d,H.h);}}else{S(t,d,H.h);}}else if(H.b==e){if(v!=null){byte[]k=v(t,d);if(k!=null){OpenNewResponseConnection(t,d,k);}else{S(t,d,H.h);}}
else{S(t,d,H.h);}}}}}public static bool SendData(string targetId,byte channel,string data,MyTransmitTarget transmissionTarget=MyTransmitTarget.Default){H c=(H)FindConnection(targetId,channel);if(c != null){c.SendData(data,
transmissionTarget);return true;}return false;}public static IConnection FindConnection(string targetId,byte channel){H c;for(int i=1,l=R.Count;i<l;i++){c=(H)R[i];if(c.s==targetId&&c.j==channel){return c;}}return null;}static
void S(string t,byte c,string d){((E)R[0])._k.A(new P(t,c,d,MyTransmitTarget.Default));}public static IConnection OpenNewConnection(string targetId,byte channel,MyTransmitTarget transmissionTarget=MyTransmitTarget.Default){if(
targetId!=null&&IsInitialized){for(int i=1,l=R.Count;i<l;i++){H c=(H)R[i];if(c.s==targetId){if(c.j==channel){return c;}}}H d=new H(targetId,channel);d.x.A(M.C(d,false,transmissionTarget));if(w!=null){w(d);}return d;}return null;}
public static IConnection OpenNewSecureConnection(string targetId,byte channel,byte[]encryptionHash, MyTransmitTarget transmissionTarget=MyTransmitTarget.Default){if(targetId!=null&&encryptionHash!=null&&IsInitialized){for(int i
=1,l=R.Count;i<l;i++){H c=(H)R[i];if(c.s==targetId){if(c.j==channel){return c;}}}H c0=new H(targetId,channel,encryptionHash);c0.x.A(M.C(c0,true,transmissionTarget));if (w!=null){w(c0);}return c0;}return null;}static H
OpenNewResponseConnection(string t,byte e,byte[]f){if(IsInitialized){for(int i=1,l=R.Count;i<l;i++){H c=(H)R[i];if(c.s==t){if(c.j==e){return null;}}}H d=(f==null?new H(t,e):new H(t,e,f));d.x.A(M.B(d,MyTransmitTarget.Default));if
(w!=null){w(d);}return d;}return null;}static int A;static bool B(G a) { return a.d==A;}static H C;static bool D(G p){if(p.d!=A){return false;}if(p is M){return((M)p).t==C;}else if(p is O){return((O)p).t==C;}else if(p is P){
return((P)p).n==C.s&&((P)p).q==C.j;}return false;}interface F{void Close();}public interface IConnection{string TargetID{get;}byte Channel{get;}bool IsSecure{get;}bool IsReady{get;}bool IsClosed{get;}bool SendData(string data,
MyTransmitTarget transmissionTarget=MyTransmitTarget.Default);void Close();}sealed class E:F{public Q<G>_k=new Q<G>();public void Close(){if(_k!=null){_k.Clear();_k = null;}}}sealed class H:F,IConnection{public const string a=
"connectionRequest";public const string b="connectionSecureRequest";public const int c= -2;public const string d="connectionAccept";public const int e= -3;public const string f="connectionHandshake";public const int g= -4;public
const string h="closeConnection";public const int k=q+100;public const string l="packetReceived:";public static readonly int m=l.Length;public const byte n=246;public const byte o=154;public const byte p=138;const int q=8000;
const int r=1000;public string s;public byte j;public string TargetID{get{return s;}}public byte Channel{get{return j;}}public bool IsSecure{get{return u!=null;}}public bool IsReady{get{return z==n;}}public bool IsClosed{get{
return v< -100;}}public byte[]u=null;int v=0;public Q<G> w=new Q<G>();public Q<G> x=new Q<G>();public Q<G> y{get{if(v<5&&x.Count>0){v++;return x;}else{v=0;return w;}}}public byte z=o;int E=0;int F=0;public int G{get{return ++F>q
?F=0:F;}}public H(string t,byte c){R.Add(this);s=t;j=c;}public H(string t,byte c,byte[]d):this(t,c){u=d;}public bool SendData(string a,MyTransmitTarget b=MyTransmitTarget.Default){if(a!=null&&w!=null&&z==n){w.A(M.E(this,a,b));
return true;}return false;}public void Close(){if(R.Remove(this)){if(z!=p){z=p;((E)R[0])._k.A(new O(this,MyTransmitTarget.Default));}}u=null;if(w!=null){w.Clear();w=null;}if(x!=null){x.Clear();x=null;}}public void I(){v=
-1000000000;if(L!=null){L(this);}}public void J(int a,string b,bool c){if(IsSecure!=c){return;}if(a<0){if(c){if(!J(ref b)){return;}}if(b==d){if(z==o){z=n;}A=H.c;C=this;G p=x.E(D);if(p!=null){p.p();}C=null;x.A(M.A(this,
MyTransmitTarget.Default));}else if(b==f){if(z==o){z=n;}A=e;C=this;G p=x.E(D);if(p!=null){p.p();}C=null;}else if(b==h){z=p;Close();I();}}else if(a==k){if(c){if(!J(ref b)){return;}}if(b.StartsWith(l)){if(int.TryParse(b.Substring(
m),out A)){G p = w.E(B);if(p==null){C=this;p=x.E(D);C=null;}if(p!=null){p.p();}}}}else if(K(a)){if(c){if(J(ref b)){}else{return;}}x.A(M.D(this,a,MyTransmitTarget.Default));E=a;if(z==n){r(this,b);}}else{x.A(M.D(this,a,
MyTransmitTarget.Default));}}bool J(ref string a){if(IsSecure){if(!U(ref a,u)){return false;}}else{return false;}return true;}public bool K(int p){int a=E-r;if(a<0){return p<a||p>E;}else{return p>E&&p<a;}}public override string
ToString(){return "sender id: "+s+"\nchannel: "+j+"\nsecure: "+IsSecure;}}abstract class G{public MyTransmitTarget k;public string b;public int c=N;public int d= -1;public G(int c,MyTransmitTarget b0){d=c;k=b0;}public string e(H
a,string b,byte c,int d,string e){bool f=a!=null&&a.IsSecure;if(f){g(ref e,a.u);}return(f?"[encrypted]:":"[unencrypted]:")+b+"|"+q+"|"+c+"|"+d.ToString()+">"+e;}public bool f(){bool a=n.TransmitMessage(b,k);if(a){c--;}return a;}
public static void g(ref string data,byte[]key){data=V(data,key);}public static bool h(string d,out bool e,out string f,out byte g,out int h,out string k){int a=d.IndexOf(':');if(a>0){string b=d.Substring(0,a+1);string o;if(b==
"[encrypted]:"){e=true;}else if(b=="[unencrypted]:"){e=false;}else{e=false;f=null;g=0;h=0;k=null;return false;}o=d.Substring(a+1);a=o.IndexOf('|');if(a>0){string p=o.Substring(0,a);if(p==q){int c=o.IndexOf('|',++a);if(c>0){f=
o.Substring(a,c-a);a=o.IndexOf('|',++c);if(a>0){p=o.Substring(c,a-c);if(byte.TryParse(p,out g)){c=o.IndexOf('>',++a);if(c>0){p=o.Substring(a,c-a);if(int.TryParse(p,out h)){k=o.Substring(c+1);return true;}}}}}}}}e=false;f=null;g=
0;h=0;k=null;return false;}public virtual void o(){}public virtual void p(){b=null;}}class M:G{public H t;public static M A(H a,MyTransmitTarget b){M p=new M(a,H.f,b,H.g);p.c=1;return p;}public static M B(H a,MyTransmitTarget b)
{return new M(a,H.d,b,H.e);}M(H a,string c,MyTransmitTarget d,int f):base(f,d){t=a;b=e(a,a.s,a.j,f,c);}public static M C(H a,bool b,MyTransmitTarget c){return new M(a,b,c);}M(H a,bool c,MyTransmitTarget d):base(H.c,d){t=a;b=e
(null,a.s,a.j,H.c,c?H.b:H.a);}public static M D(H a,int b,MyTransmitTarget c){return new M(a,b,c);}M(H a,int d,MyTransmitTarget f):base(H.k,f){t=a;c=1;b=e(a,a.s,a.j,H.k,H.l+d.ToString());}public static M E(H a,string b,
MyTransmitTarget c){return new M(a,b,c);}M(H a,string c,MyTransmitTarget f):base(a.G,f){t=a;b=e(a,a.s,a.j,d,c);}public override void o(){if (t!=null&&d!=H.k&&d!=H.g){t.Close();}p();}public override void p(){if(t!=null){if(t.w!=
null){t.w.Remove(this);}if(t.x!=null){t.x.Remove(this);}t=null;}base.p();}}class O:G{public H t;public O(H a,MyTransmitTarget d):base(-1,d){t=a;c=1;b=e(a,a.s,a.j,-1,H.h);}public override void o(){if(t!=null){t.z=H.p;t.Close();
t.I();t=null;}((E)R[0])._k.Remove(this);p();}}class P:G{public string n;public byte q;public P(string a,byte d,string f,MyTransmitTarget g):base(-1,g){n=a;q=d;c=1;b=e(null,a,d,-1,f);}public override void o(){((E)R[0])._k.Remove(
this);}}class Q<T>:LinkedList<T>{public void A(T a){AddFirst(a);}public T B(){T a=Last.Value;RemoveLast();return a;}public T C(){return Last.Value;}public LinkedListNode<T>D(Func<T,bool>a){LinkedListNode<T>b=First;if(b!=null){if(
a!=null){do{if(a(b.Value)){return b;}b=b.Next;}while(b!=First);}}return null;}public T E(Func<T,bool>a){LinkedListNode<T>n=D(a);T b=default(T);if(n!=null){b=n.Value;Remove(n);}return b;}}static readonly Encoding e=Encoding.ASCII;
static string V(string a,byte[]b){byte[]c=e.GetBytes(a);for(int i=0,j=0,l=c.Length,l1=b.Length;i<l;i++,j= ++j>=l1?(j=0):j){c[i]=(byte)(c[i]^(b[j]));}return Convert.ToBase64String(c);}static bool U(ref string a,byte[]b){if(W(a)){
byte[]c=Convert.FromBase64String(a);for(int i=0,j=0,l=c.Length,l1=b.Length;i<l;i++,j= ++j>=l1?(j=0):j){c[i]=(byte)(c[i]^(b[j]));}a=e.GetString(c);return true;}return false;}static bool W(string s){int l=s.Length;if(l==0){return
true;}if(l%4!=0){return false;}l=s.IndexOf('=');if(l>0){if(l<s.Length-2){return false;}}else{l=s.Length;}char c;for(int i=0;i<l;i++){c=s[i];if((c<48||c>57)&&(c<65||c>90)&&(c<97||c>122)&&(c!='+'&&c!='/')){return false;}}return
true;}}
