using LinuxSys;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Cryptography;
using static MusicBeePlugin.Plugin;

namespace MusicBeePlugin
{

    public abstract class MprisMessage
    {
        public string Event { get; set; }
    }

    public class MPRISNext : MprisMessage { }
    public class MPRISPrevious : MprisMessage { }
    public class MPRISPause : MprisMessage { }
    public class MPRISPlayPause : MprisMessage { }
    public class MPRISStop : MprisMessage { }
    public class MPRISPlay : MprisMessage { }

    public class MPRISSeek : MprisMessage
    {
        public long Duration { get; set; }
    }

    public class MprisMessageConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(MprisMessage);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            JObject obj = JObject.Load(reader);
            string eventType = (string)obj["event"];

            MprisMessage message = eventType switch
            {
                "next" => new MPRISNext(),
                "previous" => new MPRISPrevious(),
                "pause" => new MPRISPause(),
                "playpause" => new MPRISPlayPause(),
                "stop" => new MPRISStop(),
                "play" => new MPRISPlay(),
                "seek" => new MPRISSeek(),
                _ => throw new JsonSerializationException($"Unknown event type: {eventType}")
            };

            serializer.Populate(obj.CreateReader(), message);
            return message;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            JObject obj = JObject.FromObject(value);
            obj.WriteTo(writer);
        }
    }

    public class MprisMetadata
    {
        [JsonProperty("trackid")]
        public string TrackId { get; set; }

        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("artist")]
        public string Artist { get; set; }
    }

    public class TrackChangeEvent
    {
        [JsonProperty("event")]
        public string Event => "trackchange";

        [JsonProperty("metadata")]
        public MprisMetadata Metadata { get; set; }
    }

    public partial class Plugin
    {
        private MusicBeeApiInterface mbApiInterface;
        private PluginInfo about = new PluginInfo();

        private CancellationTokenSource cts = new CancellationTokenSource();

        Socket wineOut;

        public PluginInfo Initialise(IntPtr apiInterfacePtr)
        {
            mbApiInterface = new MusicBeeApiInterface();
            mbApiInterface.Initialise(apiInterfacePtr);
            about.PluginInfoVersion = PluginInfoVersion;
            about.Name = "MPRISBee";
            about.Description = "Sends MusicBee's status outside wine";
            about.Author = "Kyletsit";
            about.TargetApplication = "";   //  the name of a Plugin Storage device or panel header for a dockable panel
            about.Type = PluginType.General;
            about.VersionMajor = 0;  // your plugin version
            about.VersionMinor = 1;
            about.Revision = 0;
            about.MinInterfaceVersion = MinInterfaceVersion;
            about.MinApiRevision = MinApiRevision;
            about.ReceiveNotifications = (ReceiveNotificationFlags.PlayerEvents | ReceiveNotificationFlags.TagEvents);
            about.ConfigurationPanelHeight = 0;   // height in pixels that musicbee should reserve in a panel for config settings. When set, a handle to an empty panel will be passed to the Configure function
            return about;
        }

        public bool Configure(IntPtr panelHandle)
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
            // panelHandle will only be set if you set about.ConfigurationPanelHeight to a non-zero value
            // keep in mind the panel width is scaled according to the font the user has selected
            // if about.ConfigurationPanelHeight is set to 0, you can display your own popup window
            if (panelHandle != IntPtr.Zero)
            {
                Panel configPanel = (Panel)Panel.FromHandle(panelHandle);
                Label prompt = new Label();
                prompt.AutoSize = true;
                prompt.Location = new Point(0, 0);
                prompt.Text = "prompt:";
                TextBox textBox = new TextBox();
                textBox.Bounds = new Rectangle(60, 0, 100, textBox.Height);
                configPanel.Controls.AddRange(new Control[] { prompt, textBox });
            }
            return false;
        }
       
        // called by MusicBee when the user clicks Apply or Save in the MusicBee Preferences screen.
        // its up to you to figure out whether anything has changed and needs updating
        public void SaveSettings()
        {
            // save any persistent settings in a sub-folder of this path
            string dataPath = mbApiInterface.Setting_GetPersistentStoragePath();
        }

        // MusicBee is closing the plugin (plugin is being disabled by user or MusicBee is shutting down)
        public void Close(PluginCloseReason reason)
        {
            StopListening();
        }

        // uninstall this plugin - clean up any persisted files
        public void Uninstall()
        {
        }

        // receive event notifications from MusicBee
        // you need to set about.ReceiveNotificationFlags = PlayerEvents to receive all notifications, and not just the startup event
        public async Task ReceiveNotification(string sourceFileUrl, NotificationType type)
        {
            // perform some action depending on the notification type
            switch (type)
            {
                case NotificationType.PluginStartup:
                    Console.WriteLine($"MPRISBee D: Plugin startup");
                    uint uid = Syscalls.l_getuid();

                    try
                    {
                        string path = "/tmp/mprisbee" + Convert.ToString(uid) + "/wine-out";
                        Console.WriteLine($"MPRISBee D: Path: {path}");
                        wineOut = new Socket(path);

                        StartListening();
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"MPRISBee E: {ex}");
                    }

                    Console.WriteLine($"MPRISBee D: Plugin startuped");
                    break;

                case NotificationType.PlayStateChanged:
                    break;

                case NotificationType.TrackChanged:
                    Console.WriteLine($"MPRISBee D: Track changed");
                    string[] tags;

                    try
                    {
                        mbApiInterface.NowPlaying_GetFileTags(new[] { MetaDataType.TrackTitle, MetaDataType.Artist, MetaDataType.Album }, out tags);
                        string trackid = MakeTrackId(tags[0], tags[1], tags[2]);

                        var metadata = new MprisMetadata
                        {
                            TrackId = trackid,
                            Title = tags[0],
                            Artist = tags[1]
                        };

                        var trackChangeEvent = new TrackChangeEvent
                        {
                            Metadata = metadata
                        };

                        string json = JsonConvert.SerializeObject(trackChangeEvent);

                        wineOut.WriteStringNLTerminated(json);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"MPRISBee E: {ex}");
                    }
                    
                    break;

                case NotificationType.PlayerShuffleChanged:
                    break;

                case NotificationType.PlayerRepeatChanged:
                    break;
            }
        }

        private static string MakeTrackId(string title, string artist, string album)
        {
            return "/org/musicbee/track/" + BitConverter.ToString(
                    SHA256.Create().ComputeHash(
                            Encoding.UTF8.GetBytes(title + artist + album)
                        )
                ).Replace("-", "").ToLower();
        }

        private void StartListening()
        {
            Task.Run(() => ListenLoop(cts.Token));
        }

        private async Task ListenLoop(CancellationToken token)
        {
            var settings = new JsonSerializerSettings
            {
                Converters = new List<JsonConverter> { new MprisMessageConverter() }
            };

            try
            {
                while (!token.IsCancellationRequested)
                {
                    string json = await ReadMessageAsync();
                    if (!string.IsNullOrWhiteSpace(json))
                    {
                        try
                        {
                            MprisMessage message = JsonConvert.DeserializeObject<MprisMessage>(json, settings);
                            HandlePlayerEvent(message);
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine("Failed to parse JSON: " + ex.Message);
                        }
                    }
                }
            }
            catch (IOException ex)
            {
                Console.WriteLine("Socket I/O failed: " + ex.Message);
            }
        }

        private void StopListening()
        {
            cts.Cancel();
        }

        private Task<string> ReadMessageAsync()
        {
            return Task.Run(() => wineOut.ReadNLTerminatedString());
        }

        private void HandlePlayerEvent(MprisMessage message)
        {
            switch (message)
            {
                case MPRISNext:
                    mbApiInterface.Player_PlayNextTrack();
                    break;

                case MPRISPrevious:
                    mbApiInterface.Player_PlayPreviousTrack();
                    break;

                case MPRISPause:
                    if (mbApiInterface.Player_GetPlayState() == Plugin.PlayState.Playing)
                    {
                        mbApiInterface.Player_PlayPause();
                    }
                    break;

                case MPRISPlayPause:
                    mbApiInterface.Player_PlayPause();
                    break;

                case MPRISStop:
                    mbApiInterface.Player_Stop();
                    break;

                case MPRISPlay:
                    if (mbApiInterface.Player_GetPlayState() == Plugin.PlayState.Paused || mbApiInterface.Player_GetPlayState() == Plugin.PlayState.Stopped)
                    {
                        mbApiInterface.Player_PlayPause();
                    }
                    break;

                case MPRISSeek seek:
                    if (seek.Duration <= int.MaxValue && seek.Duration >= int.MinValue)
                    {
                        mbApiInterface.Player_SetPosition((int)seek.Duration);
                    }
                    else
                    {
                        throw new OverflowException("Duration value is too large for int.");
                    }
                    break;
            }
        }


        // return an array of lyric or artwork provider names this plugin supports
        // the providers will be iterated through one by one and passed to the RetrieveLyrics/ RetrieveArtwork function in order set by the user in the MusicBee Tags(2) preferences screen until a match is found
        //public string[] GetProviders()
        //{
        //    return null;
        //}

        // return lyrics for the requested artist/title from the requested provider
        // only required if PluginType = LyricsRetrieval
        // return null if no lyrics are found
        //public string RetrieveLyrics(string sourceFileUrl, string artist, string trackTitle, string album, bool synchronisedPreferred, string provider)
        //{
        //    return null;
        //}

        // return Base64 string representation of the artwork binary data from the requested provider
        // only required if PluginType = ArtworkRetrieval
        // return null if no artwork is found
        //public string RetrieveArtwork(string sourceFileUrl, string albumArtist, string album, string provider)
        //{
        //    //Return Convert.ToBase64String(artworkBinaryData)
        //    return null;
        //}

        //  presence of this function indicates to MusicBee that this plugin has a dockable panel. MusicBee will create the control and pass it as the panel parameter
        //  you can add your own controls to the panel if needed
        //  you can control the scrollable area of the panel using the mbApiInterface.MB_SetPanelScrollableArea function
        //  to set a MusicBee header for the panel, set about.TargetApplication in the Initialise function above to the panel header text
        //public int OnDockablePanelCreated(Control panel)
        //{
        //  //    return the height of the panel and perform any initialisation here
        //  //    MusicBee will call panel.Dispose() when the user removes this panel from the layout configuration
        //  //    < 0 indicates to MusicBee this control is resizable and should be sized to fill the panel it is docked to in MusicBee
        //  //    = 0 indicates to MusicBee this control resizeable
        //  //    > 0 indicates to MusicBee the fixed height for the control.Note it is recommended you scale the height for high DPI screens(create a graphics object and get the DpiY value)
        //    float dpiScaling = 0;
        //    using (Graphics g = panel.CreateGraphics())
        //    {
        //        dpiScaling = g.DpiY / 96f;
        //    }
        //    panel.Paint += panel_Paint;
        //    return Convert.ToInt32(100 * dpiScaling);
        //}

        // presence of this function indicates to MusicBee that the dockable panel created above will show menu items when the panel header is clicked
        // return the list of ToolStripMenuItems that will be displayed
        //public List<ToolStripItem> GetHeaderMenuItems()
        //{
        //    List<ToolStripItem> list = new List<ToolStripItem>();
        //    list.Add(new ToolStripMenuItem("A menu item"));
        //    return list;
        //}

        //private void panel_Paint(object sender, PaintEventArgs e)
        //{
        //    e.Graphics.Clear(Color.Red);
        //    TextRenderer.DrawText(e.Graphics, "hello", SystemFonts.CaptionFont, new Point(10, 10), Color.Blue);
        //}

    }
}
