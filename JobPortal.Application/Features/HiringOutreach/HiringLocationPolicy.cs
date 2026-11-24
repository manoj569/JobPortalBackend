namespace JobPortal.Application.Features.HiringOutreach;

public static class HiringLocationPolicy
{
    private static readonly (string Alias,string City)[] Cities=[("BANGALORE","BENGALURU"),("BENGALURU","BENGALURU"),("BOMBAY","MUMBAI"),("MUMBAI","MUMBAI"),
        ("GURGAON","GURUGRAM"),("GURUGRAM","GURUGRAM"),("DELHI NCR","NCR"),("NCR","NCR"),("HYDERABAD","HYDERABAD"),("PUNE","PUNE"),("CHENNAI","CHENNAI"),("NOIDA","NOIDA"),("DELHI","DELHI")];
    public static string City(string value)
    {
        var text=HiringContactPolicy.Normalize(value);
        foreach(var city in Cities) if(text.Contains(city.Alias,StringComparison.Ordinal)) return city.City;
        return "";
    }
    public static string Normalize(string value)
    {
        var city=City(value); return city.Length>0 ? city : HiringContactPolicy.Normalize(value);
    }
    public static bool India(string value)=>City(value).Length>0 || HiringContactPolicy.Normalize(value).Contains("INDIA",StringComparison.Ordinal);
    public static int Bonus(string contact,string job)
    {
        var city=City(contact); var target=City(job);
        if(city.Length>0 && city==target) return 30;
        if(city.Length>0) return 5;
        if(India(contact)) return 20;
        var text=HiringContactPolicy.Normalize(contact);
        if(text.Length==0 || text is "GLOBAL" or "WORLDWIDE" or "REMOTE") return 0;
        return India(job) ? -25 : text==HiringContactPolicy.Normalize(job) ? 30 : -15;
    }
}
