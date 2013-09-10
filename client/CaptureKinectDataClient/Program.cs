using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Kinect;
using System.IO;
using System.Threading;
using System.Configuration;
using System.Net;
using System.Net.Sockets;
using System.Timers;

namespace CaptureKinectDataClient
{
    class Program
    {
        static int frameOrderingNo = 0;

        static public Socket m_clientSocket;

        static ThreadStart job; 
        static Thread thread;

        // to calculate fps
        static int totalFrames = 0;
        static int lastFrames = 0;
        static DateTime lastTime = DateTime.Now;//DateTime.MaxValue;

        // Stream for the depth values
        static FileStream depthStream;
        static BinaryWriter depthWriter;

        // Stream for the depth info
        static FileStream depthInfoStream;
        static StreamWriter depthInfoWriter;

        // Stream for the color values
        static FileStream colorStream;
        static BinaryWriter colorWriter;

        // Stream for the color info
        static FileStream colorInfoStream;
        static StreamWriter colorInfoWriter;

        static byte[] colorPixels = new byte[1228800];
        static short[] depthPixels = new short[307200];
        static ColorImagePoint[] colorCoordinates = new ColorImagePoint[depthPixels.Length];   //mapdepthtocolor
        static char[] depthChar = null;

        // Sensor instance
        static KinectSensor _sensor;
        static bool motorRunning = true;
        static bool abortMotorThread = false;
        static System.Timers.Timer timer;

        static int angleRange;
        static int rotationSleepDuration;
        static int firstKinect;

        static void Main(string[] args)
        {
            chooseKinect();
            connectToServer();
        }

        //***** chooseKinect() *****//
        // Initializes and starts the sensor to the available Kinect
        static public void chooseKinect()
        {
            try
            {
                // Get values from config file
                angleRange = Convert.ToInt32(ConfigurationManager.AppSettings["angleRangeA"]);
                firstKinect = Convert.ToInt32(ConfigurationManager.AppSettings["KinectA"]);
                rotationSleepDuration = Convert.ToInt32(ConfigurationManager.AppSettings["rotationSleepDuration"]);

                _sensor = KinectSensor.KinectSensors[0];

                if (_sensor.Status == KinectStatus.Connected)  // Put those in finally statement???
                {
                    _sensor.DepthStream.Enable();
                    _sensor.ColorStream.Enable();
                    _sensor.Start();
                }
            }
            catch (System.IO.IOException)
            {
                // Get values from config file
                angleRange = Convert.ToInt32(ConfigurationManager.AppSettings["angleRangeB"]);
                firstKinect = Convert.ToInt32(ConfigurationManager.AppSettings["KinectB"]);
                rotationSleepDuration = Convert.ToInt32(ConfigurationManager.AppSettings["rotationSleepDuration"]);

                _sensor = KinectSensor.KinectSensors[1];

                if (_sensor.Status == KinectStatus.Connected)
                {
                    _sensor.DepthStream.Enable();
                    _sensor.ColorStream.Enable();
                    _sensor.Start();
                }
            }
        }


        //***** init() *****//
        // Initializes kinect angle and informs server that kinect is ready
        // Called when server presses 'init'
        static public void init()
        {
            string kinectId = formatKinectId(_sensor.UniqueKinectId);
            initStreamsWriters(kinectId);
            
            if (_sensor.Status == KinectStatus.Connected)
            {
                //_sensor.ElevationAngle = angleRange;

                Console.WriteLine("Kinect with id: " + _sensor.UniqueKinectId + " was initialized");

                // Inform the server that init is done
                try
                {
                    String initStrData = "Kinect with id " + _sensor.UniqueKinectId + " was initialized";
                    byte[] initByData = System.Text.Encoding.ASCII.GetBytes(initStrData);
                    m_clientSocket.Send(initByData);
                }
                catch (SocketException se)
                {
                    Console.WriteLine(se.Message);
                }

                // Wait for next command ('start')
                waitForCommand();
            }
        }


        //***** start() *****//
        // Initializes an event handler which will capture data when all frames are ready and
        // starts a new thread to rotate the motor
        // Called when server presses 'starts'
        static public void start()
        {
            
            Console.WriteLine("Sensor is starting");

            _sensor.AllFramesReady += new EventHandler<AllFramesReadyEventArgs>(_sensor_AllFramesReady);

            //job = new ThreadStart(rotateMotor);
            //thread = new Thread(job);
            //thread.Start();

            //////Thread thread = rotateMotorThread(Math.Abs(angleRange), rotationSleepDuration, firstKinect);

            waitForCommand();
        }


        //***** initStreamWriters() *****//
        // Creates the binary and text files named depending on kinect id
        static public void initStreamsWriters(string id)
        {
            depthStream = new FileStream("depthValues_" + id + ".data", FileMode.Append);
            depthWriter = new BinaryWriter(depthStream, Encoding.Unicode);

            depthInfoStream = new FileStream("depthInfo_" + id + ".txt", FileMode.Append);
            depthInfoWriter = new StreamWriter(depthInfoStream);

            colorStream = new FileStream("colorValues_" + id + ".data", FileMode.Append);
            colorWriter = new BinaryWriter(colorStream);

            colorInfoStream = new FileStream("colorInfo_" + id + ".txt", FileMode.Append);
            colorInfoWriter = new StreamWriter(colorInfoStream);
        }


        //***** rotateMotor() *****//
        // Function that runs in the parallel thread and controls the rotation of the kinect motor
        static void rotateMotor(/*int angleRange, int rotationSleepingDuration, int firstKinect*/)
        {
            if (firstKinect == 0)  // If it is the second kinect, start with delay
            {
                Thread.Sleep(rotationSleepDuration);
            }

            timer = new System.Timers.Timer(rotationSleepDuration);
            timer.Elapsed += new ElapsedEventHandler(timer_Elapsed);
            timer.Enabled = true;

            while (!abortMotorThread)
            {
                while (motorRunning)
                {
                    if ((_sensor.ElevationAngle == angleRange - 1) || (_sensor.ElevationAngle == angleRange))
                    {
                        _sensor.ElevationAngle = -angleRange;
                    }
                    else if ((_sensor.ElevationAngle == -angleRange + 1) || (_sensor.ElevationAngle == -angleRange) || (_sensor.ElevationAngle == -64))
                    {
                        _sensor.ElevationAngle = angleRange;
                    }
                }
            }
        }


        //***** timer_Elapsed() *****//
        // When rotationSleepingDuration has elapsed, it either stops or start the motor movement again accordingly
        static void timer_Elapsed(object sender, ElapsedEventArgs e)
        {
            if (motorRunning)
            {
                motorRunning = false;
            }
            else
            {
                motorRunning = true;
            }
        }


        //***** rotateMotorThread() *****//
        // This is where the motor thread starts (we are doing this because we want to pass parameters to the thread)
        //static Thread rotateMotorThread(int angleRange, int sleepingDuration, int firstKinect)
        //{
        //    //Console.WriteLine("entered thread");
        //    var t = new Thread(() => rotateMotor(angleRange, sleepingDuration, firstKinect));
        //    t.Start();
        //    return t;
        //}


        //***** sensor_AllFramesReady() *****//
        // Called when depth and color frames are synchronized
        // This is where the data gets written
        static void _sensor_AllFramesReady(object sender, AllFramesReadyEventArgs e)
        {
            if (!abortMotorThread)
            {
                DateTime startWritingDepth = DateTime.Now;

                //-------------------COLOR---------------------------------------//
                using (ColorImageFrame colorFrame = e.OpenColorImageFrame())
                {
                    if (colorFrame == null)
                    {
                        return;
                    }
                    colorFrame.CopyPixelDataTo(colorPixels);
                    
                    // ----------------------------------------> WRITE FRAME
                    colorWriter.Write(colorPixels);
                    colorWriter.Flush();
                    // ----------------------------------------> WRITE INFO FILE
                    colorInfoWriter.Write(frameOrderingNo);
                    colorInfoWriter.Write(" ");
                    colorInfoWriter.Write(colorFrame.Timestamp);
                    colorInfoWriter.Write(" ");
                    colorInfoWriter.Write(colorFrame.FrameNumber);
                    colorInfoWriter.Write(" ");
                    colorInfoWriter.Write(_sensor.ElevationAngle);
                    colorInfoWriter.Write(Environment.NewLine);
                    colorInfoWriter.Flush();
                }
                //----------------------------------------------------------------//

                //-------------------DEPTH---------------------------------------//
                using (DepthImageFrame depthFrame = e.OpenDepthImageFrame())
                {
                    if (depthFrame == null)
                    {
                        return;
                    }

                    depthFrame.CopyPixelDataTo(depthPixels);

                    _sensor.MapDepthFrameToColorFrame(depthFrame.Format, depthPixels, _sensor.ColorStream.Format, colorCoordinates); //mapdepthtocolor

                    depthChar = Array.ConvertAll(depthPixels, new Converter<short, char>(shortToChar));

                    // ----------------------------------------> WRITE FRAME
                    depthWriter.Write(depthChar);
                    depthWriter.Flush();
                    // ----------------------------------------> WRITE INFO FILE
                    depthInfoWriter.Write(frameOrderingNo);
                    depthInfoWriter.Write(" ");
                    depthInfoWriter.Write(depthFrame.Timestamp);
                    depthInfoWriter.Write(" ");
                    depthInfoWriter.Write(depthFrame.FrameNumber);
                    depthInfoWriter.Write(" ");
                    depthInfoWriter.Write(_sensor.ElevationAngle);
                    depthInfoWriter.Write(Environment.NewLine);
                    depthInfoWriter.Flush();
                }
                //----------------------------------------------------------------//
                frameOrderingNo++;
                calculateFps();

                //// Calculate time to write frame
                //DateTime stopWritingDepth = DateTime.Now;
                //TimeSpan diffDepth = stopWritingDepth.Subtract(startWritingDepth);
                //Console.WriteLine("Time to write one frame: " + diffDepth.TotalSeconds);
            }
        }


        //***** connectToServer() *****//
        // Establishes a connection to the server and after it's connected, it waits for the 'initialize command'
        static void connectToServer()
        {
            string localIP = getIP();
            string octopusIP = ConfigurationManager.AppSettings["IP"];
            string portNoString = ConfigurationManager.AppSettings["portNo"];

            try
            {
                // Create the socket instance
                m_clientSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                // Cet the remote IP address
                IPAddress ip = IPAddress.Parse(localIP);  
                int iPortNo = Convert.ToInt32(portNoString);
                // Create the end point 
                IPEndPoint ipEnd = new IPEndPoint(ip, iPortNo);
                // Connect to the remote host
                m_clientSocket.Connect(ipEnd);
                if (m_clientSocket.Connected)
                {
                    //Wait for data synchronously
                    Console.WriteLine("Connection with server established");

                    // start a new thread to wait for stop signal
                    //ThreadStart job = new ThreadStart(waitForCommand);
                    //Thread thread = new Thread(job);
                    //thread.Start();
                    
                    //wait for init command
                    waitForCommand();
                }
            }
            catch (SocketException se)
            {
                string str;
                str = "\nConnection failed, is the server running?\n" + se.Message;
                Console.WriteLine(str);

            }
        }
       

        //***** waitForCommand() *****//
        // Waits for server command and acts accordingly
        static void waitForCommand()
        {
            try
            {
                //Console.WriteLine("Waiting for server command");
                byte[] buffer = new byte[1024];
                int iRx = m_clientSocket.Receive(buffer);
                char[] chars = new char[iRx];

                System.Text.Decoder d = System.Text.Encoding.UTF8.GetDecoder();
                int charLen = d.GetChars(buffer, 0, iRx, chars, 0);
                System.String szData = new System.String(chars);

                //Console.WriteLine(szData);

                if (szData.Equals("initialize"))
                {
                    init();
                }
                else if (szData.Equals("start"))
                {
                    start();
                }
                else if (szData.Equals("stop"))
                {
                    stop();
                }              
            }
            catch (SocketException se)
            {
                Console.WriteLine(se.Message);
            }

        }


        //***** stop() *****//
        // Stops the motor, sensor and closes the files
        static void stop()
        {
            // Inform the server that app is closing
            try
            {
                String initStrData = "Kinect with id " + _sensor.UniqueKinectId + " is stopping";
                byte[] initByData = System.Text.Encoding.ASCII.GetBytes(initStrData);
                m_clientSocket.Send(initByData);
            }
            catch (SocketException se)
            {
                Console.WriteLine(se.Message);
            }

            // Stop motor
            //timer.Stop();
            //abortMotorThread = true;
            //motorRunning = false;

            if (_sensor != null)
            {
                if (_sensor.IsRunning)
                {
                    if (_sensor.AudioSource != null)
                    {
                        _sensor.AudioSource.Stop();
                    }
                    _sensor.Stop();
                }
            }           
            Console.WriteLine("Sensor stopped");

            // Close writers
            depthWriter.Close();
            depthInfoWriter.Close();
            colorWriter.Close();
            colorInfoWriter.Close();
            Console.WriteLine("Files closed");
            //GC.Collect();

            Console.WriteLine("Press any key to exit ...");
            Console.Read();
            return;
        }


        //******************** Auxiliary functions *******************//
        static public string formatKinectId(string id)
        {
            string delim = "\\";
            char[] delimiter = delim.ToCharArray();
            string[] UniqueIdSplit = null;
            UniqueIdSplit = id.Split(delimiter);
            string formattedId = UniqueIdSplit[UniqueIdSplit.Count() - 1];

            return formattedId;
        }

        static public char shortToChar(short s)
        {
            if (s < 0)
            {
                s = 0;
            }

            char ch = Convert.ToChar(/*(UInt16)*/s);
            return ch;
        }

        static String getIP()
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

        static void calculateFps()
        {
            ++totalFrames;
            var cur = DateTime.Now;
            if (cur.Subtract(lastTime) > TimeSpan.FromSeconds(1))
            {
                int frameDiff = totalFrames - lastFrames;
                lastFrames = totalFrames;
                lastTime = cur;
                Console.WriteLine("FPS: " + frameDiff);
            }
        }
        //**************************************************************//
    }
}




