using JortPob.Common;
using Microsoft.Scripting.Utils;
using NAudio.Wave;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using static JortPob.SoundManager;

namespace JortPob
{
    public class MainSoundBank
    {
        private readonly SoundBankGlobals globals;
        public readonly List<Sound> sounds;

        public MainSoundBank(SoundBankGlobals globals)
        {
            this.globals = globals;
            sounds = new();
        }

        // Returns the row id of the sound you add
        public int AddSound(string record, Sound.Type type, bool loop, bool spatialize, float volume, float pitch, string file)
        {
            if (Const.DEBUG_SKIP_SOUND) { return 5; } // see below

            /* See if this sound has already been added to the bank with the same (or similar enough) settings */
            foreach (Sound s in sounds)
            {
                if(s.IsSame(record, type, loop, spatialize, volume, pitch))
                {
                    return (int)s.id;
                }
            }

            /* Check if sound exists, if it doeesn't then just return some random number */
            if (!File.Exists(file)) { return 5; }  // yes, morrowind has scripts that just point to sound files that don't exist. returning a play id number that will likely do nothing

            /* Setup some paths */
            string wav = Path.Combine(Const.CACHE_PATH, "sound", $"{record}\\{record}.wav");
            string wem = Path.Combine(Const.CACHE_PATH, "sound", $"{record}\\{record}.wem");
            Directory.CreateDirectory(Path.GetDirectoryName(wav));

            /* If wem already created then skip conversion */
            if (!File.Exists(wem))
            {
                /* Some files are mp3 and some are wav. Convert if needed, otherwise just copy paste to cache to get ready for wem conversion */
                if (Path.GetExtension(file).ToLower() == ".mp3") { MP3toWAV(file, wav); }
                else { File.Copy(file, wav); }

                /* Convert wav to wem */
                WAVtoWEM(wav);
            }

            /* Create play/stop ids and source id for bnk to use */
            uint[] ids = globals.GetEventBnkId("s");

            Sound sound = new(record, type, loop, spatialize, volume, pitch, ids[0], ids[1], ids[2], wem, globals.NextSourceId());
            sounds.Add(sound);

            return (int)sound.id; // Return play id so script can trigger this sound effect
        }

        /* Copy paste from example code */
        /* Doesn't seem like WWISE cares a whole lot about wav format so we are just converting it without changing anything there */
        private void MP3toWAV(string fileMp3, string fileWav)
        {
            if (string.IsNullOrEmpty(fileMp3))
            {
                throw new ArgumentNullException(nameof(fileMp3), "Input MP3 file path cannot be null or empty.");
            }

            if (string.IsNullOrEmpty(fileWav))
            {
                throw new ArgumentNullException(nameof(fileWav), "Output WAV file path cannot be null or empty.");
            }

            // NAudio uses the ACM MP3 decoder that comes with Windows to decompress the MP3 data.
            using (var mp3Reader = new Mp3FileReader(fileMp3))
            {
                // Create a PCM stream from the MP3 reader. 
                // This converts the compressed audio to uncompressed PCM data.
                using (var pcmStream = WaveFormatConversionStream.CreatePcmStream(mp3Reader))
                {
                    // Write the PCM data to the output WAV file.
                    WaveFileWriter.CreateWaveFile(fileWav, pcmStream);
                }
            }
        }

        private void WAVtoWEM(string fileWav)
        {
            string wemPath = Path.ChangeExtension(fileWav, ".wem");
            string dir = Path.GetDirectoryName(wemPath);
            string file = Path.GetFileNameWithoutExtension(wemPath);

            for (int retry = 0; retry < Const.SAM_MAX_RETRY; retry++)
            {
                if (File.Exists(wemPath))
                {
                    // Audio file already exists in cache, no need to retry
                    return;
                }

                try
                {
                    // --- 3. Convert WAV to WEM (Wwise Console) ---

                    string wwiseConsolePath = Path.Combine(Const.WWISE_PATH, "WwiseConsole.exe");
                    string xmlName = $"{file}.wsources";
                    string xmlPath = Path.Combine(dir, xmlName);
                    string projectDir = Path.Combine(Const.CACHE_PATH, "wwise");
                    string projectPath = Path.Combine(projectDir, "wwise.wproj");

                    // Create XML file
                    string xmlRaw = $"""
                        <?xml version='1.0' encoding='UTF-8'?>
                        <ExternalSourcesList SchemaVersion="1" Root="{dir}"><Source Path="{file}.wav" Conversion="Vorbis Quality High" /></ExternalSourcesList>
                        """;
                    File.WriteAllText(xmlPath, xmlRaw);

                    // Create Wwise project if it doesn't exist
                    if (!File.Exists(projectPath))
                    {
                        // Wwise requires the folder to not exist for project creation
                        if (Directory.Exists(projectDir)) { Directory.Delete(projectDir, true); }

                        ProcessStartInfo createProjectInfo = new(wwiseConsolePath)
                        {
                            WorkingDirectory = Const.CACHE_PATH,
                            UseShellExecute = false,
                            CreateNoWindow = true
                        };
                        createProjectInfo.ArgumentList.AddRange(["create-new-project", $"\"{projectPath}\"", "--platform", "Windows"]);
                        Utility.ExecuteProcess(createProjectInfo);
                    }

                    // Convert wav to wem
                    ProcessStartInfo convertInfo = new(wwiseConsolePath)
                    {
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        WorkingDirectory = dir,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    };

                    string xmlRelative = Path.Combine("..", "sound", file, xmlName);
                    convertInfo.ArgumentList.AddRange(["convert-external-source", $"\"{projectPath}\"", "--source-file", xmlRelative, "--output", "Windows", $"\"{dir}\""]);
                    Utility.ExecuteProcess(convertInfo);

                    // If we reach here, both processes completed successfully (ExitCode 0)
                    if (File.Exists(wemPath))
                    {
                        return;
                    }

                    // If processes succeeded but the file isn't there, something is wrong, we retry
                    throw new FileNotFoundException($"WEM file was not found after successful conversion: {wemPath}");
                }
                catch (Exception ex)
                {
                    // Keep retrying. Don't spam log after every failed generation as it's bloat.
                    // If we fail up to MAX_RETRY then we throw an exception and print log.
                }
            }

            // Final check after all retries
            if (!File.Exists(wemPath))
            {
                Lort.Log($"Failed to convert wav '{fileWav}' despite {Const.SAM_MAX_RETRY} retry attempts.", Lort.Type.Debug);
                throw new($"Failed to convert wav '{fileWav}' despite {Const.SAM_MAX_RETRY} retry attempts.");
            }
        }

        public void Write()
        {
            /* Setup some paths */
            string dir = Path.Combine(Const.OUTPUT_PATH, "sd", "enus");
            string bnkPath = Path.Combine(dir, "cs_main.bnk");
            string bnkDir = Path.Combine(dir, "cs_main");
            string sourcePath = Path.Combine(Const.ELDEN_PATH, "game", "sd", "enus", "cs_main.bnk");
            string bnkJsonPath = Path.Combine(dir, "cs_main", "soundbank.json");
            string bnkRebuiltPath = Path.Combine(dir, "cs_main.created.bnk");

            /* Copy base game cs_main.bnk and then decompile it */
            if (File.Exists(bnkPath)) { File.Delete(bnkPath); }
            if (Directory.Exists(bnkDir)) { Directory.Delete(bnkDir, true); }
            Directory.CreateDirectory(Path.GetDirectoryName(bnkPath));
            File.Copy(sourcePath, bnkPath, true);

            ProcessStartInfo decompBnkProcess = new(Utility.ResourcePath(@"tools\Bnk2Json\bnk2json.exe"), $"\"{bnkPath}\"")
            {
                WorkingDirectory = Utility.ResourcePath(@"tools\Bnk2Json"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Utility.ExecuteProcess(decompBnkProcess, false);

            /* Open the generated json of the cs_main bank and append our new sounds to it */
            /* I've decided to make this stuf embeded because I can't fucking make it work as streaming and I don't care anymore reeeeeee */
            JsonNode json = JsonNode.Parse(System.IO.File.ReadAllText(bnkJsonPath));
            JsonArray templateJson = JsonNode.Parse(System.IO.File.ReadAllText(Utility.ResourcePath(@"sound\smain_template.json"))).AsArray();
            JsonArray sections = json["sections"].AsArray();
            JsonNode BKHD = sections[0]["body"]["BKHD"];
            JsonNode HIRC = sections[1]["body"]["HIRC"];
            JsonArray objects = HIRC["objects"].AsArray();

            List<JsonNode> sources = new(), containers = new(), events = new();

            JsonNode templatePlay = templateJson[0];
            JsonNode templatePlayCall = templateJson[1];
            JsonNode templateStop = templateJson[2];
            JsonNode templateStopCall = templateJson[3];
            JsonNode templateContainer = templateJson[4];
            JsonNode templateData = templateJson[5];

            /* Find main mixer so we can add children to it */
            JsonNode mixer = null;
            foreach(JsonNode obj in objects)
            {
                if (obj["id"]["Hash"] != null && obj["id"]["Hash"].GetValue<uint>() == 61849273) { mixer = obj; break; }
            }
            if(mixer == null) { throw new Exception("Could not find mixer object in cs_main sound bank!"); } // this better be unreachable

            foreach (Sound sound in sounds)
            {
                /* Copy template json objects */
                JsonNode soundPlayEvent = templatePlay.DeepClone();
                JsonNode soundPlayCall = templatePlayCall.DeepClone();
                JsonNode soundStopEvent = templateStop.DeepClone();
                JsonNode soundStopCall = templateStopCall.DeepClone();
                JsonNode container = templateContainer.DeepClone();
                JsonNode soundData = templateData.DeepClone();

                /* Generate some ids real quick */
                container["id"]["Hash"] = globals.NextBnkId();

                /* Fill out fields for all objects */
                uint sourceId = sound.source;
                soundData["id"]["Hash"] = globals.NextBnkId();
                soundData["body"]["Sound"]["bank_source_data"]["media_information"]["source_id"] = sourceId;
                soundData["body"]["Sound"]["node_base_params"]["direct_parent_id"] = container["id"]["Hash"].GetValue<uint>();

                container["body"]["RandomSequenceContainer"]["children"]["items"].AsArray()[0] = soundData["id"]["Hash"].GetValue<uint>();
                container["body"]["RandomSequenceContainer"]["playlist"]["items"].AsArray()[0]["play_id"] = soundData["id"]["Hash"].GetValue<uint>();
                container["body"]["RandomSequenceContainer"]["node_base_params"]["direct_parent_id"] = mixer["id"]["Hash"].GetValue<uint>();

                mixer["body"]["ActorMixer"]["children"]["items"].AsArray().Add(container["id"]["Hash"].GetValue<uint>());

                soundPlayCall["id"]["Hash"] = globals.NextBnkId();
                soundPlayCall["body"]["Action"]["external_id"] = container["id"]["Hash"].GetValue<uint>();
                soundPlayCall["body"]["Action"]["params"]["Play"]["bank_id"] = BKHD["bank_id"].GetValue<uint>();
                soundStopCall["id"]["Hash"] = globals.NextBnkId();
                soundStopCall["body"]["Action"]["external_id"] = container["id"]["Hash"].GetValue<uint>();

                soundPlayEvent["id"]["String"] = $"Play_{sound.GetPrefix()}{sound.id:D8}0";
                soundPlayEvent["body"]["Event"]["actions"][0] = soundPlayCall["id"]["Hash"].GetValue<uint>();
                soundStopEvent["id"]["String"] = $"Stop_{sound.GetPrefix()}{sound.id:D8}0";
                soundStopEvent["body"]["Event"]["actions"][0] = soundStopCall["id"]["Hash"].GetValue<uint>();

                sources.Add(soundData);
                containers.Add(container);
                events.Add(soundPlayCall);
                events.Add(soundStopCall);
                events.Add(soundPlayEvent);
                events.Add(soundStopEvent);

                string wemSrcPath = sound.file;
                string wemTgtPath = Path.Combine(dir, @$"wem\{sourceId.ToString("D9").Substring(0, 2)}\{sourceId:D9}.wem");   // old code where we write to loose wems
                Directory.CreateDirectory(Path.GetDirectoryName(wemTgtPath));
                if (File.Exists(wemTgtPath)) { File.Delete(wemTgtPath); }
                File.Copy(wemSrcPath, wemTgtPath);
                string wemEmbedTgtPath = Path.Combine(dir, "cs_main", $"{sourceId}.wem");                                     // new code where we write to embeded wems
                Directory.CreateDirectory(Path.GetDirectoryName(wemEmbedTgtPath));
                if (File.Exists(wemEmbedTgtPath)) { File.Delete(wemEmbedTgtPath); }
                File.Copy(wemSrcPath, wemEmbedTgtPath);
            }

            foreach (JsonNode node in containers) { objects.Insert(0, node); } // after sources but before main mixer
            foreach (JsonNode node in sources) { objects.Insert(0, node); } // must be added at top
            objects.AddRange(events);  // added at bottom

            var temp = mixer["body"]["ActorMixer"]["children"]["items"].AsArray();
            var sorted = temp.Select(x => x.GetValue<uint>()).ToList();
            sorted.Sort();
            temp.Clear();
            sorted.ForEach(temp.Add);

            HIRC["object_count"] = objects.Count;
            mixer["body"]["ActorMixer"]["children"]["count"] = mixer["body"]["ActorMixer"]["children"]["items"].AsArray().Count;

            Directory.CreateDirectory(Path.GetDirectoryName(bnkJsonPath));
            File.WriteAllText(bnkJsonPath, json.ToJsonString());

            ProcessStartInfo recompBnkProcess = new(Utility.ResourcePath(@"tools\Bnk2Json\bnk2json.exe"), $"\"{Path.GetDirectoryName(bnkJsonPath)}\"")
            {
                WorkingDirectory = Utility.ResourcePath(@"tools\Bnk2Json"),
                UseShellExecute = false,
                CreateNoWindow = true
            };
            Utility.ExecuteProcess(recompBnkProcess, false);

            if (File.Exists(bnkPath)) { File.Delete(bnkPath); }
            File.Move(bnkRebuiltPath, bnkPath);
        }

        [DebuggerDisplay("SND [{record}] [{type}] [{file}]")]
        public record Sound(
            string record,             // id of the source sound. this is the sound record id for "playsound" and the filename for "say" papyrus calls
            Sound.Type type,
            bool loop,                  // play constantly until explicitly stopped
            bool spatialize,            // if true = 3d sound, false = direct speaker play
            float volume,               // these appear to be unused by morrowind but i'm including them
            float pitch,                // these appear to be unused by morrowind but i'm including them
            uint id,                    // id used for script calls to playback this sound
            uint play,
            uint stop,
            string file,                // wem file
            uint source                 // source is wem id
        )
        {
            public enum Type { Voice, SFX }

            public string GetPrefix()
            {
                switch (type)
                {
                    case Type.Voice: return "v";
                    case Type.SFX: return "s";
                    default: throw new Exception("Invalid sound type"); // How did you even get here?
                }
            }

            public bool IsSame(string r, Sound.Type t, bool l, bool s, float v, float p)
            {
                return
                    record.ToLower().Trim() == r.ToLower().Trim() &&
                    type == t &&
                    loop == l &&
                    spatialize == s &&
                    (volume - v) <= 0.01f &&
                    (pitch - p) <= 0.01f;
            }
        }
    }
}
