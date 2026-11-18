using System.Text.Json;

internal sealed record Settings
{
    public int SchemaVersion {get;set;}=2;
    public string Display {get;set;}="";
    public int Frequency {get;set;}=5;
    public int Strength {get;set;}=60;
    public int CenterWeight {get;set;}=40;
    public int TransitionMs {get;set;}=300;
    public double MaxGamma {get;set;}=1.6;
    public int Brightness {get;set;}=6;
    public int Contrast {get;set;}=8;
    public bool GameOnly {get;set;}=true;
    public static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TarkovAutoGamma");
    public static string RecoveryPath=>Path.Combine(Folder,"recovery.json");
    public static Settings Load()
    {
        try{return Parse(File.ReadAllText(Path.Combine(Folder,"settings.json")));}catch{return new();}
    }
    internal static Settings Parse(string json)
    {
        var s=JsonSerializer.Deserialize<Settings>(json)??new();using var doc=JsonDocument.Parse(json);
        if(!doc.RootElement.TryGetProperty(nameof(SchemaVersion),out var version) || version.GetInt32()<2)
            if(s.TransitionMs==1200)s.TransitionMs=300;
        s.Normalize();return s;
    }
    public void Normalize()
    {
        Frequency=new[]{2,5,10}.Contains(Frequency)?Frequency:5;Strength=Math.Clamp(Strength,0,100);
        CenterWeight=new[]{20,40,60}.Contains(CenterWeight)?CenterWeight:40;TransitionMs=Math.Clamp(TransitionMs,100,4000);
        MaxGamma=double.IsFinite(MaxGamma)?Math.Clamp(MaxGamma,1,2):1.6;Display??="";
        Brightness=Math.Clamp(Brightness,-20,20);Contrast=Math.Clamp(Contrast,-30,30);SchemaVersion=2;
    }
    public void Save(){Directory.CreateDirectory(Folder);var path=Path.Combine(Folder,"settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));File.Move(path+".tmp",path,true);}
}
