using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace GraphicalKinectServer
{
    public partial class MainWindow : Window
    {
        const int MAX_CLIENTS = 10;

        public AsyncCallback pfnWorkerCallBack;
        private Socket m_mainSocket;
        private Socket[] m_workerSocket = new Socket[10];
        private int m_clientCount = 0;

        public MainWindow()
        {            
            InitializeComponent();
            this.Title = "Kinect Server";
            serverIPtext.Text = GetIP();
        }

        String GetIP()
        {
            String strHostName = Dns.GetHostName();

            // Find host by name
            IPHostEntry iphostentry = Dns.GetHostByName(strHostName);

            // Grab the first IP addresses
            String IPStr = "";
            foreach (IPAddress ipaddress in iphostentry.AddressList)
            {
                IPStr = ipaddress.ToString();
                return IPStr;
            }
            return IPStr;
        }

        private void startListeningBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Check the port value
                if (portText.Text == "")
                {
                    MessageBox.Show("Please enter a Port Number");
                    return;
                }
                string portStr = portText.Text;
                int port = System.Convert.ToInt32(portStr);
                // Create the listening socket...
                m_mainSocket = new Socket(AddressFamily.InterNetwork,
                                          SocketType.Stream,
                                          ProtocolType.Tcp);
                IPEndPoint ipLocal = new IPEndPoint(IPAddress.Any, port);
                // Bind to local IP Address...
                m_mainSocket.Bind(ipLocal);
                // Start listening...
                m_mainSocket.Listen(4);
                // Create the call back for any client connections...
                m_mainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);

                statusText.Inlines.Add("Server is listening ...");

                UpdateControls(true);

            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }
        }


        public void OnClientConnect(IAsyncResult asyn)
        {
            try
            {
                // Here we complete/end the BeginAccept() asynchronous call
                // by calling EndAccept() - which returns the reference to
                // a new Socket object
                m_workerSocket[m_clientCount] = m_mainSocket.EndAccept(asyn);
                // Let the worker Socket do the further processing for the 
                // just connected client
                WaitForData(m_workerSocket[m_clientCount]);
                // Now increment the client count
                ++m_clientCount;
                // Display this client connection as a status message on the GUI	
                String str = String.Format("Client # {0} connected", m_clientCount);

                Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("\n")), null);
                Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add(str)), null);

                // Since the main Socket is now free, it can go back and wait for
                // other clients who are attempting to connect
                m_mainSocket.BeginAccept(new AsyncCallback(OnClientConnect), null);
            }
            catch (ObjectDisposedException)
            {
                System.Diagnostics.Debugger.Log(0, "1", "\n OnClientConnection: Socket has been closed\n");
            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }

        }

        public class SocketPacket
        {
            public System.Net.Sockets.Socket m_currentSocket;
            public byte[] dataBuffer = new byte[1024];
        }

        public void WaitForData(System.Net.Sockets.Socket soc)
        {
            //Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("\n")), null);
            //Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("waiting for data")), null);

            try
            {
                if (pfnWorkerCallBack == null)
                {
                    // Specify the call back function which is to be 
                    // invoked when there is any write activity by the 
                    // connected client
                    pfnWorkerCallBack = new AsyncCallback(OnDataReceived);
                }
                SocketPacket theSocPkt = new SocketPacket();
                theSocPkt.m_currentSocket = soc;
                // Start receiving any data written by the connected client
                // asynchronously
                soc.BeginReceive(theSocPkt.dataBuffer, 0,
                                   theSocPkt.dataBuffer.Length,
                                   SocketFlags.None,
                                   pfnWorkerCallBack,
                                   theSocPkt);
            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }

        }

        public void OnDataReceived(IAsyncResult asyn)
        {
            try
            {
                SocketPacket socketData = (SocketPacket)asyn.AsyncState;

                int iRx = 0;
                // Complete the BeginReceive() asynchronous call by EndReceive() method
                // which will return the number of characters written to the stream 
                // by the client
                iRx = socketData.m_currentSocket.EndReceive(asyn);
                char[] chars = new char[iRx + 1];
                System.Text.Decoder d = System.Text.Encoding.UTF8.GetDecoder();
                int charLen = d.GetChars(socketData.dataBuffer,
                                         0, iRx, chars, 0);
                System.String szData = new System.String(chars);

                //Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("\n")), null);
                //Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add(szData.Length.ToString())), null);

                Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("\n")), null);
                Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add(szData)), null);

                // Continue the waiting for data on the Socket
                WaitForData(socketData.m_currentSocket);
            }
            catch (ObjectDisposedException)
            {
                System.Diagnostics.Debugger.Log(0, "1", "\nOnDataReceived: Socket has been closed\n");
            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }
        }

        private void UpdateControls(bool listening)
        {
            startListeningBtn.IsEnabled = !listening;
            stopListeningBtn.IsEnabled = listening;
        }

        private void initBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string initString = "initialize";
                byte[] byData = System.Text.Encoding.ASCII.GetBytes(initString);
                for (int i = 0; i < m_clientCount; i++)
                {
                    if (m_workerSocket[i] != null)
                    {
                        if (m_workerSocket[i].Connected)
                        {
                            m_workerSocket[i].Send(byData);
                        }
                    }
                }

            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }
        }

        private void startBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string initString = "start";
                byte[] byData = System.Text.Encoding.ASCII.GetBytes(initString);
                for (int i = 0; i < m_clientCount; i++)
                {
                    if (m_workerSocket[i] != null)
                    {
                        if (m_workerSocket[i].Connected)
                        {
                            m_workerSocket[i].Send(byData);
                        }
                    }
                }

            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }
        }

        private void stopBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string initString = "stop";
                byte[] byData = System.Text.Encoding.ASCII.GetBytes(initString);
                for (int i = 0; i < m_clientCount; i++)
                {
                    if (m_workerSocket[i] != null)
                    {
                        if (m_workerSocket[i].Connected)
                        {
                            m_workerSocket[i].Send(byData);
                        }
                    }
                }
                
            }
            catch (SocketException se)
            {
                MessageBox.Show(se.Message);
            }
        }

        private void stopListeningBtn_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("\n")), null);
            Application.Current.Dispatcher.BeginInvoke(new ThreadStart(() => statusText.Inlines.Add("Server stopped listening ...")), null);
            CloseSockets();
            UpdateControls(false);
        }

        void CloseSockets()
        {
            if (m_mainSocket != null)
            {
                m_mainSocket.Close();
            }
            for (int i = 0; i < m_clientCount; i++)
            {
                if (m_workerSocket[i] != null)
                {
                    m_workerSocket[i].Close();
                    m_workerSocket[i] = null;
                }
            }
        }
    }


}
