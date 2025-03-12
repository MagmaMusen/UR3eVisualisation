using System.Collections.Generic;

public static class DefaultSettings
{
    public static class Values
    {
        public static int Port = 5556;
        public static float Speed = 20f;
        public static float Stiffness = 100000f;
        public static float Damping = 10000f;
        public static float ForceLimit = 10000f;
    }
    
    public static class Keys
    {
        public static string Port = "Port";
        public static string Speed = "Speed";
        public static string Stiffness =  "Stiffness";
        public static string Damping = "Damping";
        public static string ForceLimit = "ForceLimit";
    }

    public static Dictionary<string, string> Lookup = new Dictionary<string, string>
    {
        { Keys.Port, Values.Port.ToString() },
        { Keys.Speed, Values.Speed.ToString() },
        { Keys.Stiffness, Values.Stiffness.ToString() },
        { Keys.Damping, Values.Damping.ToString() },
        { Keys.ForceLimit, Values.ForceLimit.ToString() }
    };
}
