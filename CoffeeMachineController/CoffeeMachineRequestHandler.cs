using Maple;
using System;
using System.Collections;

namespace CoffeeMachineController
{
    public class CoffeeMachineRequestHandler : RequestHandlerBase
    {
        public void getStatus()
        {

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

        private void SendSuccessStatusResponse()
        {
            Context.Response.ContentType = "application/json";
            Context.Response.StatusCode = 200;
            Send();
        }
        private void SendSuccessStatusResponse(Hashtable result)
        {
            Context.Response.ContentType = "application/json";
            Context.Response.StatusCode = 200;
            Send(result);
        }
    }
}
