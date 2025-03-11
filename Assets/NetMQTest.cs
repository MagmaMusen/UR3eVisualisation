using System.Collections.Concurrent;
using System.Threading;
using NetMQ;
using UnityEngine;
using NetMQ.Sockets;
using UnityEngine.Events;

public class ClientObject : MonoBehaviour
{
    public string topic = "actual_q";
    public string port = "5556";
    private NetMqListener _netMqListener;
    public UnityEvent<int, float> UpdateJP;

    private void HandleMessage(string message)
    {
        string[] msgSplit = message.Split(' ');
        string msg_topic = msgSplit[0];
        string msg = msgSplit[1];
        int jpIndex = int.Parse(msg_topic[msg_topic.Length - 1].ToString());
        float jp = float.Parse(msg);

        UpdateJP.Invoke(jpIndex, jp);
    }

    private void Start()
    {
        _netMqListener = new NetMqListener(HandleMessage, topic, port);
        _netMqListener.Start();
    }

    private void Update()
    {
        _netMqListener.Update();
    }

    private void OnDestroy()
    {
        _netMqListener.Stop();
    }
}

public class NetMqListener
{
    private readonly Thread _listenerWorker;

    private bool _listenerCancelled;

    private string _topic;

    private string _port;

    public delegate void MessageDelegate(string message);

    private readonly MessageDelegate _messageDelegate;

    private readonly ConcurrentQueue<string> _messageQueue = new ConcurrentQueue<string>();

    private void ListenerWork()
    {
        AsyncIO.ForceDotNet.Force();
        using (var subSocket = new SubscriberSocket())
        {
            subSocket.Options.ReceiveHighWatermark = 1000;
            subSocket.Connect($"tcp://localhost:{_port}");
            subSocket.Subscribe(_topic);
            while (!_listenerCancelled)
            {
                string frameString;
                if (!subSocket.TryReceiveFrameString(out frameString)) continue;
                _messageQueue.Enqueue(frameString);
            }
            subSocket.Close();
        }
        NetMQConfig.Cleanup();
    }

    public void Update()
    {
        while (!_messageQueue.IsEmpty)
        {
            string message;
            if (_messageQueue.TryDequeue(out message))
            {
                _messageDelegate(message);
            }
            else
            {
                break;
            }
        }
    }

    public NetMqListener(MessageDelegate messageDelegate, string topic, string port)
    {
        _messageDelegate = messageDelegate;
        _topic = topic;
        _port = port;
        _listenerWorker = new Thread(ListenerWork);
    }

    public void Start()
    {
        _listenerCancelled = false;
        _listenerWorker.Start();
    }

    public void Stop()
    {
        _listenerCancelled = true;
        _listenerWorker.Join();
    }
}