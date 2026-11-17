using System.Text.Json;

internal sealed record Settings
{
    public string Display {get;set;}="";
    public int Frequency {get;set;}=5;
    public int Strength {get;set;}=60;
    public int CenterWeight {get;set;}=40;
    public int TransitionMs {get;set;}=1200;
    public double MaxGamma {get;set;}=1.6;
    public bool GameOnly {get;set;}=true;
    public static string Folder=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"TarkovAutoGamma");
    public static string RecoveryPath=>Path.Combine(Folder,"recovery.json");
    public static Settings Load()
    {
        try{var s=JsonSerializer.Deserialize<Settings>(File.ReadAllText(Path.Combine(Folder,"settings.json")))??new();s.Normalize();return s;}catch{return new();}
    }
    public void Normalize()
    {
        Frequency=new[]{2,5,10}.Contains(Frequency)?Frequency:5;Strength=Math.Clamp(Strength,0,100);
        CenterWeight=new[]{20,40,60}.Contains(CenterWeight)?CenterWeight:40;TransitionMs=Math.Clamp(TransitionMs,300,4000);
        MaxGamma=double.IsFinite(MaxGamma)?Math.Clamp(MaxGamma,1,2):1.6;Display??="";
    }
    public void Save(){Directory.CreateDirectory(Folder);var path=Path.Combine(Folder,"settings.json");File.WriteAllText(path+".tmp",JsonSerializer.Serialize(this));File.Move(path+".tmp",path,true);}
}
