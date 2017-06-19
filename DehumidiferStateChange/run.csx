#r "System.Runtime.Serialization"
#r "Microsoft.WindowsAzure.Storage"
#r "Newtonsoft.Json"

using System.Collections.Generic;
using System.Configuration;
using System.Net;
using System.Runtime.Serialization;
using Newtonsoft.Json;
using Microsoft.WindowsAzure.Storage.Blob;
using Twilio;
using Twilio.Rest.Api.V2010.Account;    
using Twilio.Types;

static bool shouldSaveState = false;

public enum TankState
{
    Unknown,
    Empty,
    Full
}

public class DehumidState
{
    public TankState TankState { get; set; }
    public DateTime LastChange { get; set; }
    public List<string> NotificationNumbers { get; set; }
}

public class ParticlePayload
{
    [DataMember(Name = "data")]
    public string Data { get; set; }

    [DataMember(Name = "name")]
    public string Name { get; set; }
}

public static async Task<string> Run(ParticlePayload req, ICloudBlob inputBlob, TraceWriter log)
{
    var state = await ReadStateFromBlob(inputBlob);
    if (state == null)
        state = new DehumidState();
    
    // Read the input state
    if (req.Name != "dehumid/state")
        return "Wrong event";
    
    TankState currentTankState = (TankState) int.Parse(req.Data);
    log.Info("Current tank state: " + currentTankState);
    log.Info("Previous tank state: " + state.TankState);

    if (currentTankState != state.TankState)
    {
        log.Info("Tank state changed! New state: " + currentTankState);

        await SendSmsRegardingTankState(currentTankState, state.NotificationNumbers);

        state.TankState = currentTankState;
        state.LastChange = DateTime.UtcNow;
        shouldSaveState = true;
    }

    //state.T = DateTime.UtcNow;

    if (shouldSaveState)
        await SaveStateToBlob(inputBlob, state);

    return "OK";
}

static Dictionary<TankState, string> TextMessages = new Dictionary<TankState, string>
{
    {TankState.Unknown, "Uh, this isn't suppose to happen"},
    {TankState.Empty, "The Dehumidifer tank is now empty"},
    {TankState.Full, "The Dehumidifer tank is full and needs to be emptied"}
};

public static async Task SendSmsRegardingTankState(TankState currentTankState, IEnumerable<string> phoneNumbers)
{
    string message = TextMessages[currentTankState];

    TwilioClient.Init(
        ConfigurationManager.AppSettings["Twilio_SID"],
        ConfigurationManager.AppSettings["Twilio_Secret"]
    );

    var senderNumber = new PhoneNumber(ConfigurationManager.AppSettings["Twilio_Number"]);

    foreach (string number in phoneNumbers)
    {
        await MessageResource.CreateAsync(
            from: senderNumber,
            to: new PhoneNumber(number),
            body: message
        );
    }
}

public static async Task<DehumidState> ReadStateFromBlob(ICloudBlob blob)
{
    string text;
    using (var memoryStream = new MemoryStream())
    {
        await blob.DownloadToStreamAsync(memoryStream);
        text = System.Text.Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    return JsonConvert.DeserializeObject<DehumidState>(text);
}

public static async Task SaveStateToBlob(ICloudBlob blob, DehumidState state)
{
    string text = JsonConvert.SerializeObject(state);
    byte[] bytes = System.Text.Encoding.UTF8.GetBytes(text);

    await blob.Upload​From​Byte​Array​Async(bytes, 0, bytes.Length);
}
