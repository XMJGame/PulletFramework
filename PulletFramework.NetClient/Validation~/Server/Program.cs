using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Collections.Concurrent;
using PulletNet.NetCore.Core;
using PulletNet.NetCore.Events;

var root=Path.GetFullPath(args[0]);
var runtimes=new List<NetCoreServerRuntime>();
var listener=new EchoListener();
using var deadline=new CancellationTokenSource(TimeSpan.FromMinutes(8));
try {
 var ports=new Dictionary<string,int>();
 foreach(var mode in new[]{"udp","tcp","ws"}) {
  int port;
  if(mode=="udp"){using var probe=new UdpClient(0);port=((IPEndPoint)probe.Client.LocalEndPoint!).Port;}
  else{var probe=new TcpListener(IPAddress.Loopback,0);probe.Start();port=((IPEndPoint)probe.LocalEndpoint).Port;probe.Stop();}
  var builder=NetCoreBuilder.CreateServer(new NetCoreOptions {Session=new(){ConnectionMode=mode=="udp"?ConnectionMode.UdpOnly:ConnectionMode.TransportOnly}}).UseEventListener(listener);
  if(mode=="udp")builder.UseUdp(port);else if(mode=="tcp")builder.UseTcp(port);else builder.UseWebSocket(port,"/ws","127.0.0.1");
  var runtime=builder.BuildServer();runtimes.Add(runtime);
  var result=await runtime.StartAsync(deadline.Token);
  if(!result.IsSuccess)throw new InvalidOperationException($"{mode} start: {result}");
  ports.Add(mode,port);
 }
 File.WriteAllText(Path.Combine(root,"ports.json"),JsonSerializer.Serialize(ports));
 Console.WriteLine("READY "+JsonSerializer.Serialize(ports));
 while(!File.Exists(Path.Combine(root,"stop-server")))await Task.Delay(100,deadline.Token);
} finally {
 var failures=new List<string>();
 foreach(var runtime in runtimes.AsEnumerable().Reverse()){
  try{var result=await runtime.StopAsync();if(!result.IsSuccess)failures.Add(result.ToString());await runtime.DisposeAsync();}
  catch(Exception ex){failures.Add(ex.ToString());}
 }
 File.WriteAllText(Path.Combine(root,"server-result.json"),JsonSerializer.Serialize(new{failures,echoes=listener.Echoes,sendFailures=listener.Failures.ToArray()}));
 if(failures.Count!=0||!listener.Failures.IsEmpty)throw new InvalidOperationException("Server cleanup/echo failure; see receipt.");
}
sealed class EchoListener:NetCoreEventListener {
 public int Echoes;public ConcurrentQueue<string> Failures=new();
 public override void OnReceived(in NetCoreReceivedEvent e){
  var sent=e.Session.SendAsync(e.Payload,e.ChannelId??0);
  if(!sent.IsCompletedSuccessfully)throw new InvalidOperationException("Expected synchronous bounded send admission");
  var result=sent.Result;if(!result.IsSuccess)Failures.Enqueue(result.ToString());else Interlocked.Increment(ref Echoes);
 }
}
