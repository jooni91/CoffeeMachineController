using Json.NETMF;
using Maple;
using System;
using System.Collections;

namespace CoffeeMachineController
{
    public class CoffeeMachineRequestHandler : RequestHandlerBase
    {
        public void getStatus()
        {
            SendSuccessStatusResponse(JsonSerializer.SerializeObject(Application.Instance.RequestCoffeeMachineStatus()));
        }

        public void postTurnOn()
        {
            TurnOffMode mode = TurnOffMode.Automatic;
            int turnOffMs = 0;
            int delayBrew = 0;

            // Parse any parameters of the request that exist
            if (QueryString.Contains("mode"))
                if (QueryString["mode"].ToString() == "manual")
                    mode = TurnOffMode.Manual;

            if (QueryString.Contains("turnoffin"))
                turnOffMs = Convert.ToInt32(QueryString["turnoffin"].ToString());

            if (QueryString.Contains("delaybrew"))
                delayBrew = Convert.ToInt32(QueryString["delaybrew"].ToString());

            Application.Instance?.RequestTurnOnCoffeeMachine(mode, turnOffMs, delayBrew);
            SendSuccessStatusResponse();
        }

        public void postTurnOff()
        {
            Application.Instance?.RequestTurnOffCoffeeMachine();
            SendSuccessStatusResponse();
        }

        public void postChangeDelay()
        {
            int delay = 0;

            // Parse any parameters of the request that exist
            if (QueryString.Contains("minutes"))
                delay = Convert.ToInt32(QueryString["minutes"].ToString());

            Application.Instance?.RequestChangeBrewingDelay(delay);
            SendSuccessStatusResponse();
        }

        private void SendSuccessStatusResponse()
        {
            Context.Response.ContentType = "application/json";
            Context.Response.StatusCode = 200;
            Send();
        }
        private void SendSuccessStatusResponse(string jsonString)
        {
            Context.Response.ContentType = "application/json";
            Context.Response.StatusCode = 200;
            Send(jsonString);
        }
    }
}
