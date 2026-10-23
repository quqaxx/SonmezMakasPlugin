using AssettoServer.Network.ClientMessages;

namespace SonmezMakasPlugin;

// Alan adları s01, s02... diye sıralı: Lua tarafındaki sıra ile birebir aynı olmalı.

[OnlineEvent(Key = "SonmezMakasState")]
public class StatePacket : OnlineEvent<StatePacket>
{
    [OnlineEventField(Name = "s01score")]   public int Score;
    [OnlineEventField(Name = "s02total")]   public int Total;
    [OnlineEventField(Name = "s03combo")]   public byte Combo;
    [OnlineEventField(Name = "s04pending")] public int Pending;
    [OnlineEventField(Name = "s05race")]    public byte Race;      // 0 yok, 1 sayım, 2 yarış, 3 sonuç
    [OnlineEventField(Name = "s06cd")]      public byte Countdown;
    [OnlineEventField(Name = "s07prog")]    public float Progress; // metre
    [OnlineEventField(Name = "s08place")]   public byte Place;
    [OnlineEventField(Name = "s09racers")]  public byte Racers;
    [OnlineEventField(Name = "s10winner")]  public byte Winner;
    [OnlineEventField(Name = "s11wtime")]   public float WinnerTime;
    [OnlineEventField(Name = "s12t1")]      public byte Top1;
    [OnlineEventField(Name = "s13t1s")]     public int Top1Score;
    [OnlineEventField(Name = "s14t2")]      public byte Top2;
    [OnlineEventField(Name = "s15t2s")]     public int Top2Score;
    [OnlineEventField(Name = "s16t3")]      public byte Top3;
    [OnlineEventField(Name = "s17t3s")]     public int Top3Score;
    [OnlineEventField(Name = "s18dist")]    public float RaceDistance;
}

[OnlineEvent(Key = "SonmezMakasInput")]
public class InputPacket : OnlineEvent<InputPacket>
{
    [OnlineEventField(Name = "s01horn")]  public byte Horn;
    [OnlineEventField(Name = "s02ang")]   public byte Angle;   // 0..180 derece
    [OnlineEventField(Name = "s03crash")] public byte Crash;
}
