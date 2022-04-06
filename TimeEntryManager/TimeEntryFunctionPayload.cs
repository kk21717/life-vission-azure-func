using System;

namespace TimeEntryManager
{
    public class TimeEntryFunctionPayload
    {

        public TimeEntryFunctionPayload(){}

        public TimeEntryFunctionPayload(DateOnly startOn, DateOnly endOn)
        {
            StartOn = startOn;
            EndOn = endOn;
        }

        public DateOnly StartOn { get; set; }
        public DateOnly EndOn { get; set; }

        public string ToJson()
        {
            return "{ \"StartOn\" : \"" + StartOn.ToString("yyyy-MM-dd") + "\" , \"EndOn\" : \"" + EndOn.ToString("yyyy-MM-dd") + "\" }";
        }
    }
}