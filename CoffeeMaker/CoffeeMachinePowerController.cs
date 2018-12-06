using System;
using Microsoft.SPOT;
using System.Net.Sockets;
using System.Net;
using System.Threading;
using Microsoft.SPOT.Hardware;
using SecretLabs.NETMF.Hardware.Netduino;
using Microsoft.SPOT.Net.NetworkInformation;

namespace CoffeeMaker
{
    public enum CoffeeState
    {
        Brewing,
        Standby,
        None
    }
    public enum ControllerMode
    {
        Manual,
        Automatic
    }

    public class CoffeeMachinePowerController
    {
        /// <summary>
        /// Milliseconds before automatically closing the machine
        /// </summary>
        public const int TURN_OFF_COFFEE_IN_MS = 60000;

        private Timer TurnOffCoffeeTimer { get; set; }
        private Timer ControllerModeTimer { get; set; }
        private TimeSpan ModeUptime { get; set; }

        public CoffeeState CoffeeState { get; set; }
        public ControllerMode ControllerMode { get; set; }

        public void StartController()
        {
            CoffeeState = CoffeeState.Standby;
            ControllerMode = ControllerMode.Automatic;

            ControllerModeTimer = new Timer(IncreaseUptimeTimer, null, 1000, Timeout.Infinite);

            // write your code here
            // setup the LED and turn it off by default
            Led = new OutputPort(Pins.ONBOARD_LED, false);

            // configure the port # (the standard web server port is 80)
            int port = 80;
            // wait a few seconds for the Netduino Plus to get a network address.
            Thread.Sleep(5000);

            // display the IP address
            NetworkInterface networkInterface = NetworkInterface.GetAllNetworkInterfaces()[0];

            Debug.Print("my ip address: " + networkInterface.IPAddress);
            
            // create a socket to listen for incoming connections and the listener end point
            var listenerSocket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            IPEndPoint listenerEndPoint = new IPEndPoint(IPAddress.Any, port);

            // bind to the listening socket
            listenerSocket.Bind(listenerEndPoint);

            // and start listening for incoming connections
            listenerSocket.Listen(1);

            // listen for and process incoming requests
            while (true)
            {
                // wait for a client to connect
                Socket clientSocket = listenerSocket.Accept();

                // wait for data to arrive
                bool dataReady = clientSocket.Poll(5000000, SelectMode.SelectRead);

                // if dataReady is true and there are bytes available to read,
                // then you have a good connection.
                if (dataReady && clientSocket.Available > 0)
                {
                    var buffer = new byte[clientSocket.Available];
                    int bytesRead = clientSocket.Receive(buffer);
                    var request = new string(System.Text.Encoding.UTF8.GetChars(buffer));

                    if (request.IndexOf("START") >= 0 && ControllerMode == ControllerMode.Automatic)
                    {
                        this.TurnOnCoffee();
                        this.GetStatusCoffee(clientSocket);
                    }
                    else if (request.IndexOf("STOP") >= 0 && ControllerMode == ControllerMode.Automatic)
                    {
                        this.TurnOffCoffee(null);
                        this.GetStatusCoffee(clientSocket);
                        Thread.Sleep(2000);
                        PowerState.RebootDevice(false);
                    }
                    else if (request.IndexOf("STATUS") >= 0)
                    {
                        this.GetStatusCoffee(clientSocket);
                    }
                    else if (request.IndexOf("MANUAL") >= 0 && ControllerMode == ControllerMode.Automatic)
                    {
                        this.TurnManualModeOn();
                        this.GetStatusCoffee(clientSocket);
                    }
                    else if (request.IndexOf("AUTOMATIC") >= 0 && ControllerMode == ControllerMode.Manual)
                    {
                        ControllerMode = ControllerMode.Automatic;
                        this.GetStatusCoffee(clientSocket);
                        Thread.Sleep(2000);
                        PowerState.RebootDevice(false);
                    }
                }
                // important: close the client socket
                clientSocket.Close();
            }
        }

        /// <summary>
        /// Close the coffeemaker automatically
        /// </summary>
        public void ResetTimerToTurnOffCoffee()
        {
            if (this.TurnOffCoffeeTimer != null)
                this.TurnOffCoffeeTimer.Change(TURN_OFF_COFFEE_IN_MS, Timeout.Infinite);
            else
                this.TurnOffCoffeeTimer = new Timer(TurnOffCoffee, null, TURN_OFF_COFFEE_IN_MS, Timeout.Infinite);

        }

        /// <summary>
        /// Get the status of the coffee maker
        /// </summary>
        /// <param name="clientSocket"></param>
        public void GetStatusCoffee(Socket clientSocket)
        {
            string statusText = String.Empty;
            if (ControllerMode == ControllerMode.Manual)
            {
                statusText = "The coffee machine is in manual mode.";
                statusText += "The uptime in the automatic mode is " + ModeUptime.Hours + ":" + ModeUptime.Minutes + ":" + ModeUptime.Seconds;
            }
            else
            {
                statusText = "The coffee machine is in automatic mode and is " + (CoffeeState == CoffeeState.Brewing ? "Brewing" : "Standby");
                statusText += "The uptime in the automatic mode is " + ModeUptime.Hours + ":" + ModeUptime.Minutes + ":" + ModeUptime.Seconds;
            }

            // return a message to the client letting it 
            // know if the LED is now on or off.
            string response =
                "HTTP/1.1 200 OK\r\n" +
                "Content-Type: text/html; charset=utf-8\r\n\r\n" +
                "<html><head><title>Friezzinger Coffee Machine</title></head>" +
                "<body><h1>" + statusText + "</></body></html>";

            clientSocket.Send(System.Text.Encoding.UTF8.GetBytes(response));
        }

        /// <summary>
        /// Open the coffee maker
        /// </summary>
        public void TurnOnCoffee()
        {
            this.CoffeeState = CoffeeState.Brewing;
            Led.Write(true);

            if (Relay == null)
                Relay = new OutputPort(Pins.GPIO_PIN_D9, true);

            this.ResetTimerToTurnOffCoffee();
        }

        /// <summary>
        /// Close the coffee maker
        /// </summary>
        public void TurnOffCoffee(object o)
        {
            this.CoffeeState = CoffeeState.Standby;
            Led.Write(false);
        }

        public void TurnManualModeOn()
        {
            this.CoffeeState = CoffeeState.None;
            this.ControllerMode = ControllerMode.Manual;
            Led.Write(true);

            if (Relay == null)
                Relay = new OutputPort(Pins.GPIO_PIN_D9, true);

            ModeUptime = new TimeSpan();
            this.TurnOffCoffeeTimer.Dispose();
            this.TurnOffCoffeeTimer = null;
        }

        public void IncreaseUptimeTimer(object o)
        {
            if (ModeUptime == null)
                ModeUptime = new TimeSpan();

            ModeUptime.Add(new TimeSpan(0, 0, 1));
        }
    }
}
