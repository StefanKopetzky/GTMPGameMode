using GrandTheftMultiplayer.Server.API;
using GrandTheftMultiplayer.Server.Elements;
using GrandTheftMultiplayer.Shared;
using PureVoice;
using NLog;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace VoiceSupport
{
    public class VoiceController : Script
    {
        static Logger logger = LogManager.GetCurrentClassLogger();
        static System.Timers.Timer teamspeakTimer;
        static ConcurrentDictionary<int, Dictionary<string, VoiceLocationInformation>> PlayerHears = new ConcurrentDictionary<int, Dictionary<string, VoiceLocationInformation>>();
        static PureVoice.Server.VoiceServer _voiceServer;

        public static string VoiceServerIP = "";
        public static int VoiceServerPort = 4500;
        public static string VoiceServerSecret = "";
        public static string VoiceServerGUID = "";
        public static ulong VoiceDefaultChannel = 1;
        public static ulong VoiceIngameChannel = 115;
        public static int VoiceClientPort = 4239;
        public static string VoiceIngameChannelPassword = "egal";
        public static Version VoiceServerPluginVersion = new Version(0, 0, 0, 0);
        public static bool VoiceEnableLipSync = true;
        public static float VoiceMaxRange = 50.0f;
        public static bool VoiceForceVoice = true;

        public VoiceController() : base()
        {
            API.onResourceStart += API_onResourceStart;
        }

        private T GetVoiceSetting<T>(string name, bool throwError = false, T defaultValue = default(T))
        {
            if (API.hasSetting(name))
                return API.getSetting<T>(name);
            if (throwError)
                throw new Exception(String.Format("Please configure '{0}' in the meta.xml!", name));
            return defaultValue;
        }

        private void API_onResourceStart()
        {

            VoiceDefaultChannel = GetVoiceSetting<ulong>("voice_defaultchannel", true);
            VoiceIngameChannel = GetVoiceSetting<ulong>("voice_ingamechannel", true);
            VoiceIngameChannelPassword = GetVoiceSetting<string>("voice_ingamechannelpassword", false);
            VoiceServerGUID = GetVoiceSetting<string>("voice_serverguid", true);
            VoiceServerIP = GetVoiceSetting<string>("voice_server", true);
            VoiceServerPort = GetVoiceSetting<int>("voice_port", true);
            VoiceServerSecret = GetVoiceSetting<string>("voice_secret", true);
            VoiceClientPort = GetVoiceSetting<int>("voice_clientport", true);
            Version.TryParse(GetVoiceSetting<string>("voice_minpluginversion", false), out VoiceServerPluginVersion);
            VoiceEnableLipSync = GetVoiceSetting<bool>("voice_enablelipsync", false, true);
            VoiceMaxRange = GetVoiceSetting<float>("voice_maxrange", false, 50.0f);
            VoiceForceVoice = GetVoiceSetting<bool>("voice_forcevoice", false, true);

            _voiceServer = new PureVoice.Server.VoiceServer(VoiceServerPort, VoiceServerSecret, VoiceServerGUID, VoiceServerPluginVersion,
                VoiceDefaultChannel, VoiceIngameChannel, VoiceIngameChannelPassword, VoiceEnableLipSync);
            _voiceServer.VoiceClientConnected += _voiceServer_VoiceClientConnected;
            _voiceServer.VoiceClientDisconnected += _voiceServer_VoiceClientDisconnected;
            _voiceServer.VoiceClientOutdated += _voiceServer_VoiceClientOutdated;
            _voiceServer.VoiceClientTalking += _voiceServer_VoiceClientTalking;
            _voiceServer.VoiceClientMicrophoneStatusChanged += _voiceServer_VoiceClientMicrophoneStatusChanged;
            _voiceServer.VoiceClientSpeakersStatusChanged += _voiceServer_VoiceClientSpeakersStatusChanged;

            teamspeakTimer = API.delay(200, false, () => UpdateTeamspeak());

            API.onPlayerDeath += API_onPlayerDeath;
            API.onPlayerFinishedDownload += API_onPlayerFinishedDownload;
            API.onPlayerDisconnected += API_onPlayerDisconnected;

        }

        private void API_onPlayerFinishedDownload(Client player)
        {
            PlayerHears[player.handle.Value] = new Dictionary<string, VoiceLocationInformation>();
            Connect(player);
        }

        private void API_onPlayerDisconnected(Client player, string reason)
        {
            PlayerHears[player.handle.Value] = new Dictionary<string, VoiceLocationInformation>();
            if (player.hasData("VOICE_ID"))
                _voiceServer.SendCommand(Convert.ToInt64(player.getData("VOICE_ID")), "DISCONNECT", "");
            player.resetData("VOICE_ID");
            player.resetData("VOICE_TS_ID");
        }

        private void API_onPlayerDeath(Client player, NetHandle entityKiller, int weapon)
        {
            Dictionary<string, VoiceLocationInformation> notused = null;
            PlayerHears.TryRemove(player.handle.Value, out notused);
            _voiceServer.MutePlayer(player.getData("VOICE_ID"), "_ALL_");
        }

        private Client GetPlayerByConnectionId(long connectionId)
        {
            return API.getAllPlayers().ToList().FirstOrDefault(p => p.hasData("VOICE_ID") && p.getData("VOICE_ID") == connectionId);
        }

        private string GetTeamspeakID(Client streamedPlayer)
        {
            if (!streamedPlayer.hasData("VOICE_TEAMSPEAK_IDENT"))
                return String.Empty;
            return streamedPlayer.getData("VOICE_TEAMSPEAK_IDENT");
        }

        private ushort GetTeamspeakClientID(Client streamedPlayer)
        {
            if (!streamedPlayer.hasData("VOICE_TS_ID"))
                return 0;
            return streamedPlayer.getData("VOICE_TS_ID");
        }

        // LipSync
        private void _voiceServer_VoiceClientTalking(long connectionId, bool isTalking)
        {
            var p = GetPlayerByConnectionId(connectionId);
            if (p != null)
            {
                var pPos = p.position;
                var pls = API.shared.getAllPlayers().ToList().Where(c => c.position.DistanceTo2D(pPos) < 20).ToList();
                if (isTalking)
                    pls.ForEach(pt => pt.triggerEvent("LIPSYNC", p, "mp_facial", "mic_chatter", true));
                else
                    pls.ForEach(pt => pt.triggerEvent("LIPSYNC", p, "facials@gen_male@variations@normal", "mood_normal_1", true));
            }
        }

        private void _voiceServer_VoiceClientOutdated(string clientGUID, Version hisVersion, Version ourVersion)
        {
            var p = API.getAllPlayers().ToList().FirstOrDefault(c => c.socialClubName == clientGUID);
            if (p != null)
            {
                p.kick("Please update your TS3 PureVoice Plugin.");
            }
        }

        private void _voiceServer_VoiceClientDisconnected(long connectionId)
        {
            var p = GetPlayerByConnectionId(connectionId);
            if (p != null)
            {
                p.resetData("VOICE_ID");
                p.resetData("VOICE_TS_ID");
                p.resetData("VOICE_TEAMSPEAK_IDENT");
                if (VoiceForceVoice)
                    p.setData("VOICE_TIMEOUT", DateTime.Now.AddMinutes(1));
                API.triggerResourceEvent("PUREVOICE_CLIENT_DISCONNECTED", p);
            }
        }

        private void _voiceServer_VoiceClientSpeakersStatusChanged(long connectionId, bool isMuted)
        {
            var p = GetPlayerByConnectionId(connectionId);
            if (p != null)
            {
                logger.Debug("{0} Speakers muted {1}", p.name, isMuted);
                API.triggerResourceEvent("PUREVOICE_CLIENT_SPEAKERSTATUS", p, isMuted);
            }
        }

        private void _voiceServer_VoiceClientMicrophoneStatusChanged(long connectionId, bool isMuted)
        {
            var p = GetPlayerByConnectionId(connectionId);
            if (p != null)
            {
                logger.Debug("{0} Mic muted {1}", p.name, isMuted);
                API.triggerResourceEvent("PUREVOICE_CLIENT_MICROPHONESTATUS", p, isMuted);
            }
        }

        private void _voiceServer_VoiceClientConnected(string clientGUID, string teamspeakID, ushort teamspeakClientID, long connectionID, string clientName, bool micMuted, bool speakersMuted)
        {
            var p = API.getAllPlayers().ToList().FirstOrDefault(c => c.socialClubName == clientGUID);
            if (p != null)
            {
                logger.Debug("VoiceConnect {0} {1} {2} {3}", p.socialClubName, teamspeakID, teamspeakClientID, connectionID);
                _voiceServer.ConfigureClient(connectionID, p.name, false);
                p.setData("VOICE_ID", connectionID);
                p.setData("VOICE_TS_ID", teamspeakClientID);
                p.setData("VOICE_TEAMSPEAK_IDENT", teamspeakID);
                API.triggerResourceEvent("PUREVOICE_CLIENT_CONNECTED", p, micMuted, speakersMuted);
            }
        }


        /* Teamspeak Handling Functions */
        public static void Connect(Client player)
        {
            player.triggerEvent("PUREVOICE", VoiceServerIP, VoiceServerPort, VoiceServerSecret, player.socialClubName, VoiceServerPluginVersion.ToString(), VoiceClientPort);
            if (VoiceForceVoice)
                player.setData("VOICE_TIMEOUT", DateTime.Now.AddMinutes(1));
        }

        public void UpdateTeamspeak()
        {
            var players = API.getAllPlayers().ToList();
            players.ForEach(p => { try { UpdateTeamspeakForUser(p, players); } catch (Exception ex) { logger.Error(ex); } });
        }


        public void UpdateTeamspeakForUser(Client player, List<Client> allPlayers)
        {
            var playerPos = player.position;
            var playerRot = player.rotation;
            var rotation = Math.PI / 180 * (playerRot.Z * -1);
            var playerVehicle = player.vehicle;
            var cId = player.handle.Value;

            if (!player.hasData("VOICE_ID"))
            {
                if (player.hasData("VOICE_TIMEOUT") && (player.getData("VOICE_TIMEOUT") < DateTime.Now))
                {
                    API.triggerResourceEvent("PUREVOICE_CLIENT_NOVOICE", player);
                    player.kick("Please install the TS3 PureVoice Plugin");
                }
                return;
            }
            var targetId = player.getData("VOICE_ID");

            var playersIHear = new Dictionary<string, VoiceLocationInformation>();

            // Players near me
            var inRangePlayers = allPlayers.Where(cl => (cl != player) && (cl.position.DistanceTo2D(playerPos) <= VoiceMaxRange) && (cl.dimension == player.dimension) && cl.hasData("VOICE_TS_ID")).ToList();

            if (inRangePlayers != null)
            {
                foreach (var streamedPlayer in inRangePlayers)
                {
                    var n = GetTeamspeakID(streamedPlayer);
                    if (streamedPlayer == player)
                    {
                        continue;
                    }

                    var streamedPlayerPos = streamedPlayer.position;
                    var distance = playerPos.DistanceTo(streamedPlayerPos);
                    var range = 5; // VoiceRange of streamed player in meters, maybe make this a player-Data?
                    var volumeModifier = 0f;

                    if (distance <= range)
                    {
                        if (Math.Abs(streamedPlayerPos.Z - playerPos.Z) > range)
                        {
                            continue;
                        }

                        var subPos = streamedPlayerPos.Subtract(playerPos);

                        var x = subPos.X * Math.Cos(rotation) - subPos.Y * Math.Sin(rotation);
                        var y = subPos.X * Math.Sin(rotation) + subPos.Y * Math.Cos(rotation);

                        if (distance > 2)
                        {
                            volumeModifier = (float)(distance / range) * -10f;
                        }
                        if (volumeModifier > 0)
                        {
                            volumeModifier = 0;
                        }
                        volumeModifier = Math.Min(10.0f, Math.Max(-10.0f, volumeModifier));
                        if (!playersIHear.ContainsKey(n))
                            playersIHear[n] = new VoiceLocationInformation(n, GetTeamspeakClientID(streamedPlayer));
                        playersIHear[n].Update(new TSVector((float)(Math.Round(x * 1000) / 1000), (float)(Math.Round(y * 1000) / 1000), 0), (float)(Math.Round(volumeModifier * 1000) / 1000), false);
                    }
                }
            }


            // Players in same vehicle are always near
            if (player.isInVehicle)
            {
                var others = player.vehicle.occupants.ToList();
                if (others != null)
                {
                    others.Remove(player);
                    others.ForEach(p =>
                    {

                        var tsId = GetTeamspeakID(p);
                        if (!playersIHear.ContainsKey(tsId))
                            playersIHear[tsId] = new VoiceLocationInformation(tsId, GetTeamspeakClientID(p));
                        playersIHear[tsId].Update(new TSVector(0, 0, 0), 4, true);
                    });
                }
            }

            PlayerHears[player.handle.Value] = playersIHear;

            _voiceServer.SendUpdate(targetId, playersIHear.Count > 0 ? playersIHear.Values.ToList() : new List<VoiceLocationInformation>());
        }


    }
}
