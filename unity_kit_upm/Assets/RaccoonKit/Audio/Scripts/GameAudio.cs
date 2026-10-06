using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace Raccoon.Audio
{
    /// <summary>
    /// Manager script to play audio on different channels, two sounds can't play on the same channel.
    /// Useful to avoid having sounds play on top of each other
    /// </summary>

    public class GameAudio : MonoBehaviour
    {
        private static GameAudio instance;
        public static GameAudio Instance => instance;

        public const float DEFAULT_SFX_VOLUME = 0.7f;
        public const float DEFAULT_MUSIC_VOLUME = 0.3f;
        public const float DEFAULT_CLICK_VOLUME = 0.4f;
        public const float DEFAULT_DUCK_VOLUME = 0.3f;
        public const string BG_MUSIC_CHANNEL = "bg_music";
        private const string CLICK_CHANNEL = "button_click";
        private const string ONESHOT_CHANNEL = "oneshot";
        private const int MAX_ONESHOT_SOURCES = 8;
        private const float DUCK_FADE_TIME = 0.25f;

        private const string KEY_SOUND_ENABLED = "GameAudio_SoundEnabled";
        private const string KEY_MUSIC_ENABLED = "GameAudio_MusicEnabled";
        private const string KEY_SFX_ENABLED = "GameAudio_SfxEnabled";
        private const string KEY_MASTER_VOLUME = "GameAudio_MasterVolume";
        private const string KEY_SFX_VOLUME = "GameAudio_SfxVolume";
        private const string KEY_MUSIC_VOLUME = "GameAudio_MusicVolume";

        [SerializeField] private AudioLibrary library;
        public AudioClip clickSound;
        [FormerlySerializedAs("musicBG")]
        [SerializeField] private AudioClip[] bgMusics;
        [SerializeField] private float bgMusicFadeDuration = 1f;

        private bool isSoundEnabled = true;
        private bool isMusicEnabled = true;
        private bool isSfxEnabled = true;
        private float masterVolume = 1f;
        private float sfxVolume = 1f;
        private float musicVolume = 1f;
        private bool settingsDirty;

        public bool IsSoundEnabled => isSoundEnabled;
        public bool IsMusicEnabled => isMusicEnabled;
        public bool IsSfxEnabled => isSfxEnabled;
        public float MasterVolume => masterVolume;
        public float SfxVolume => sfxVolume;
        public float MusicVolume => musicVolume;

        private bool CanPlayMusic => isSoundEnabled && isMusicEnabled;
        private bool CanPlaySfx => isSoundEnabled && isSfxEnabled;

        private Dictionary<string, AudioSource> channels_sfx = new Dictionary<string, AudioSource>();
        private Dictionary<string, AudioSource> channels_music = new Dictionary<string, AudioSource>();
        private Dictionary<string, float> channels_sfx_volume = new Dictionary<string, float>();
        private Dictionary<string, float> channels_music_volume = new Dictionary<string, float>();

        private List<AudioSource> oneShotSources = new List<AudioSource>();
        private int oneShotIndex;

        private HashSet<string> userPausedMusic = new HashSet<string>(); //Paused by PauseMusic, only ResumeMusic/PlayMusic resumes it
        private HashSet<string> suspendedMusic = new HashSet<string>(); //Should be playing but music is disabled, resumed when music is enabled again
        private bool bgPlaylistActive;
        private int bgMusicIndex = -1;

        private class MusicFade
        {
            public float from;
            public float to;
            public float duration;
            public float elapsed;
            public System.Action onComplete;
        }

        private Dictionary<string, MusicFade> musicFades = new Dictionary<string, MusicFade>();
        private Dictionary<string, float> channels_music_fade = new Dictionary<string, float>();
        private List<string> fadeKeysBuffer = new List<string>();

        private float duckCurrent = 1f;
        private float duckLevel = 1f;
        private float duckEndTime;
        private List<AudioSource> duckSources = new List<AudioSource>();

        private HashSet<string> suspendedSfx = new HashSet<string>(); //Looping sounds paused because sound effects are disabled
        private Dictionary<string, float> suspendedDuckSfx = new Dictionary<string, float>(); //Duck level of suspended looping sounds that were ducking the music
        private HashSet<string> warnedMissing = new HashSet<string>();

        void Awake()
        {
            if (instance != null)
            {
                Destroy(gameObject); return;
            }
            instance = this;
            DontDestroyOnLoad(gameObject);
            LoadSettings();
        }

        private void OnDestroy()
        {
            if (instance == this)
            {
                SaveSettings();
                instance = null;
            }
        }

        private void OnApplicationPause(bool pause)
        {
            if (pause)
                SaveSettings();
        }

        private void OnApplicationQuit()
        {
            SaveSettings();
        }

        private void Update()
        {
            UpdateFades();
            UpdateDuck();
            UpdateBGPlaylist();
        }

        private void LoadSettings()
        {
            isSoundEnabled = PlayerPrefs.GetInt(KEY_SOUND_ENABLED, 1) == 1;
            isMusicEnabled = PlayerPrefs.GetInt(KEY_MUSIC_ENABLED, 1) == 1;
            isSfxEnabled = PlayerPrefs.GetInt(KEY_SFX_ENABLED, 1) == 1;
            masterVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_MASTER_VOLUME, 1f));
            sfxVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_SFX_VOLUME, 1f));
            musicVolume = Mathf.Clamp01(PlayerPrefs.GetFloat(KEY_MUSIC_VOLUME, 1f));
        }

        //Writing to disk is slow on mobile, so settings are only flushed on pause/quit/destroy instead of on every change
        public void SaveSettings()
        {
            if (!settingsDirty)
                return;
            PlayerPrefs.SetInt(KEY_SOUND_ENABLED, isSoundEnabled ? 1 : 0);
            PlayerPrefs.SetInt(KEY_MUSIC_ENABLED, isMusicEnabled ? 1 : 0);
            PlayerPrefs.SetInt(KEY_SFX_ENABLED, isSfxEnabled ? 1 : 0);
            PlayerPrefs.SetFloat(KEY_MASTER_VOLUME, masterVolume);
            PlayerPrefs.SetFloat(KEY_SFX_VOLUME, sfxVolume);
            PlayerPrefs.SetFloat(KEY_MUSIC_VOLUME, musicVolume);
            PlayerPrefs.Save();
            settingsDirty = false;
        }

        private float FinalSfxVolume(float vol) => vol * sfxVolume * masterVolume;
        private float FinalMusicVolume(float vol) => vol * musicVolume * masterVolume;

        private static float RandomPitch(float pitchRandom) => pitchRandom > 0f ? 1f + Random.Range(-pitchRandom, pitchRandom) : 1f;

        //channel: Two sounds on the same channel will never play at the same time, sounds on different channel will play at the same time.
        //priority: if false, will not play if a sound is already playing on the channel, if true, will replace current sound playing on channel
        //pitchRandom: pitch is randomized in [1 - pitchRandom, 1 + pitchRandom] so repeated sounds feel less monotone (ex: 0.1)
        //Returns true if the sound was played
        public bool PlaySFX(string channel, AudioClip sound, float vol = DEFAULT_SFX_VOLUME, bool priority = true, bool loop = false, float pitchRandom = 0f)
        {
            if (!CanPlaySfx || string.IsNullOrEmpty(channel) || sound == null)
                return false;

            AudioSource source = GetChannel(channel);
            if (source == null)
            {
                source = CreateChannel(channel); //Create channel if doesnt exist
                channels_sfx[channel] = source;
            }

            if (!priority && source.isPlaying)
                return false;

            duckSources.Remove(source); //The previous sound of this channel is replaced, so it no longer ducks the music
            suspendedSfx.Remove(channel);
            suspendedDuckSfx.Remove(channel);
            channels_sfx_volume[channel] = vol;
            source.clip = sound;
            source.volume = FinalSfxVolume(vol);
            source.pitch = RandomPitch(pitchRandom);
            source.loop = loop;
            source.Play();
            return true;
        }

        //Plays the sound and lowers the music to duckVolume (0-1 of its current volume) while the sound is playing
        public bool PlaySFXWithDuck(string channel, AudioClip sound, float vol = DEFAULT_SFX_VOLUME, float duckVolume = DEFAULT_DUCK_VOLUME)
        {
            if (!PlaySFX(channel, sound, vol))
                return false;

            DuckWhilePlaying(GetChannel(channel), duckVolume);
            return true;
        }

        //Plays with PlayOneShot on a small pool of sources, sounds overlap instead of cutting each other (ex: collecting many coins quickly)
        public bool PlaySFXOneShot(AudioClip sound, float vol = DEFAULT_SFX_VOLUME, float pitchRandom = 0f)
        {
            if (!CanPlaySfx || sound == null)
                return false;

            AudioSource source = GetOneShotSource();
            source.volume = FinalSfxVolume(1f);
            source.pitch = RandomPitch(pitchRandom);
            source.PlayOneShot(sound, vol);
            return true;
        }

        //Plays a sound from the AudioLibrary with the settings of its entry
        public bool PlaySFX(string id)
        {
            SoundEntry entry = null;
            if (library == null || !library.TryGetSound(id, out entry))
            {
                WarnMissing("Sound", id);
                return false;
            }

            AudioClip clip = entry.GetRandomClip();
            if (clip == null)
                return false;

            bool oneShot = string.IsNullOrEmpty(entry.channel);
            bool played = oneShot
                ? PlaySFXOneShot(clip, entry.volume, entry.pitchRandom)
                : PlaySFX(entry.channel, clip, entry.volume, true, entry.loop, entry.pitchRandom);

            if (played && entry.duckMusic)
            {
                if (oneShot)
                    DuckMusic(DEFAULT_DUCK_VOLUME, clip.length / Mathf.Max(0.1f, 1f - entry.pitchRandom)); //Longest duration the clip can take with the lowest pitch
                else
                    DuckWhilePlaying(GetChannel(entry.channel), DEFAULT_DUCK_VOLUME); //Also covers looping sounds
            }
            return played;
        }

        //Logs once per id so a missing sound called often (ex: on every click) doesn't flood the console
        private void WarnMissing(string kind, string id)
        {
            string key = library == null ? "" : kind + ":" + id;
            if (!warnedMissing.Add(key))
                return;

            if (library == null)
                Debug.LogWarning("[GameAudio] AudioLibrary is not assigned, sounds and musics can't be played by id", this);
            else
                Debug.LogWarning($"[GameAudio] {kind} id '{id}' not found in {library.name}", this);
        }

        //Plays a music from the AudioLibrary with the settings of its entry
        public void PlayMusic(string id, float fadeDuration = 0f)
        {
            MusicEntry entry = null;
            if (library == null || !library.TryGetMusic(id, out entry))
            {
                WarnMissing("Music", id);
                return;
            }

            string channel = string.IsNullOrEmpty(entry.channel) ? BG_MUSIC_CHANNEL : entry.channel;
            PlayMusic(channel, entry.clip, entry.volume, entry.loop, fadeDuration);
        }

        //Pitch is per source, so each one shot needs a free source to not change the pitch of sounds already playing
        private AudioSource GetOneShotSource()
        {
            foreach (AudioSource source in oneShotSources)
            {
                if (source != null && !source.isPlaying)
                    return source;
            }

            if (oneShotSources.Count < MAX_ONESHOT_SOURCES)
            {
                AudioSource created = CreateChannel(ONESHOT_CHANNEL + oneShotSources.Count);
                oneShotSources.Add(created);
                return created;
            }

            //All sources busy, reuse them in turn
            oneShotIndex = (oneShotIndex + 1) % oneShotSources.Count;
            if (oneShotSources[oneShotIndex] == null)
                oneShotSources[oneShotIndex] = CreateChannel(ONESHOT_CHANNEL + oneShotIndex);
            return oneShotSources[oneShotIndex];
        }

        //channel: Two sounds on the same channel will never play at the same time, sounds on different channel will play at the same time.
        //If music is already playing on the same channel, new music will be played unless its the same one.(Won't restart in that case, only volume/loop are updated)
        //fadeDuration: when switching music, the current one fades out during the first half and the new one fades in during the second half
        //If music is disabled, the music is kept and will start when music is enabled again
        public void PlayMusic(string channel, AudioClip music, float vol = DEFAULT_MUSIC_VOLUME, bool loop = true, float fadeDuration = 0f)
        {
            if (string.IsNullOrEmpty(channel) || music == null)
                return;

            AudioSource source = GetMusicChannel(channel);
            if (source == null)
            {
                source = CreateChannel(channel); //Create channel if doesnt exist
                channels_music[channel] = source;
            }

            if (channel == BG_MUSIC_CHANNEL)
                bgPlaylistActive = false; //A chosen music replaces the random bgMusics playlist, PlayNextBGMusic turns it back on

            channels_music_volume[channel] = vol;
            source.loop = loop;
            userPausedMusic.Remove(channel);

            if (CanPlayMusic && source.clip == music && source.isPlaying)
            {
                //Same music already playing: a pending switch or stop is cancelled and the volume comes back smoothly, a fade in keeps going
                if (musicFades.TryGetValue(channel, out MusicFade pending) && pending.onComplete != null)
                    StartFade(channel, 1f, fadeDuration * 0.5f, null);
                else
                    ApplyMusicVolume(channel);
                return;
            }

            CompleteFade(channel); //Finish any pending transition before starting a new one

            if (!CanPlayMusic)
            {
                if (source.clip != music)
                {
                    source.Stop();
                    source.clip = music;
                }
                SetFade(channel, 1f);
                suspendedMusic.Add(channel);
                return;
            }

            if (source.clip != music && source.isPlaying && fadeDuration > 0f)
            {
                float half = fadeDuration * 0.5f;
                StartFade(channel, 0f, half, () => StartMusic(channel, source, music, half));
                return;
            }

            StartMusic(channel, source, music, fadeDuration);
        }

        private void StartMusic(string channel, AudioSource source, AudioClip music, float fadeInDuration)
        {
            if (source.clip != music)
            {
                source.clip = music;
                source.Play();
            }
            else
            {
                ResumeOrPlay(source);
            }

            if (fadeInDuration > 0f)
            {
                SetFade(channel, 0f);
                StartFade(channel, 1f, fadeInDuration, null);
            }
            else
            {
                SetFade(channel, 1f);
            }
        }

        public void PauseMusic(string channel)
        {
            AudioSource source = GetMusicChannel(channel);
            if (source == null)
                return;

            CompleteFade(channel);
            if (!source.isPlaying && !suspendedMusic.Contains(channel))
                return;

            source.Pause();
            userPausedMusic.Add(channel);
            suspendedMusic.Remove(channel);
        }

        //Continues music paused with PauseMusic from where it stopped
        public void ResumeMusic(string channel)
        {
            AudioSource source = GetMusicChannel(channel);
            if (source == null || source.clip == null || !userPausedMusic.Remove(channel))
                return;

            if (CanPlayMusic)
                ResumeOrPlay(source);
            else
                suspendedMusic.Add(channel);
        }

        //fadeDuration: fades the music out before stopping it
        public void StopMusic(string channel, float fadeDuration = 0f)
        {
            AudioSource source = GetMusicChannel(channel);
            if (source == null)
                return;

            musicFades.Remove(channel); //Cancel, not complete: completing a pending switch would start the next music just to stop it
            userPausedMusic.Remove(channel);
            suspendedMusic.Remove(channel);
            if (channel == BG_MUSIC_CHANNEL)
                bgPlaylistActive = false;

            if (fadeDuration > 0f && source.isPlaying)
            {
                StartFade(channel, 0f, fadeDuration, () =>
                {
                    source.Stop();
                    SetFade(channel, 1f);
                });
            }
            else
            {
                source.Stop();
                SetFade(channel, 1f);
            }
        }

        public void StopSoundFX(string channel)
        {
            AudioSource source = GetChannel(channel);
            if (source != null)
                source.Stop();
            suspendedSfx.Remove(channel);
            suspendedDuckSfx.Remove(channel);
        }

        //Lowers all music to duckVolume (0-1 of its current volume) for duration seconds, then restores it smoothly
        public void DuckMusic(float duckVolume, float duration)
        {
            SetDuckLevel(duckVolume);
            duckEndTime = Mathf.Max(duckEndTime, Time.unscaledTime + duration);
        }

        //Lowers all music while the source is playing, works for looping sounds and any pitch
        private void DuckWhilePlaying(AudioSource source, float duckVolume)
        {
            if (source == null)
                return;

            SetDuckLevel(duckVolume);
            if (!duckSources.Contains(source))
                duckSources.Add(source);
        }

        private void SetDuckLevel(float duckVolume)
        {
            duckVolume = Mathf.Clamp01(duckVolume);
            if (IsDucking())
                duckLevel = Mathf.Min(duckLevel, duckVolume); //Keep the lowest level while several ducks overlap
            else
                duckLevel = duckVolume;
        }

        private bool IsDucking()
        {
            duckSources.RemoveAll(IsNotPlaying);
            return Time.unscaledTime < duckEndTime || duckSources.Count > 0;
        }

        private static readonly System.Predicate<AudioSource> IsNotPlaying = source => source == null || !source.isPlaying;

        private void UpdateDuck()
        {
            float target = IsDucking() ? duckLevel : 1f;
            if (duckCurrent == target)
                return;

            duckCurrent = Mathf.MoveTowards(duckCurrent, target, Time.unscaledDeltaTime / DUCK_FADE_TIME);
            foreach (string channel in channels_music.Keys)
                ApplyMusicVolume(channel);
        }

        private float GetFade(string channel)
        {
            return channels_music_fade.TryGetValue(channel, out float fade) ? fade : 1f;
        }

        private void SetFade(string channel, float fade)
        {
            channels_music_fade[channel] = fade;
            ApplyMusicVolume(channel);
        }

        private void StartFade(string channel, float to, float duration, System.Action onComplete)
        {
            musicFades.Remove(channel);
            if (duration <= 0f)
            {
                SetFade(channel, to);
                onComplete?.Invoke();
                return;
            }

            musicFades[channel] = new MusicFade { from = GetFade(channel), to = to, duration = duration, onComplete = onComplete };
        }

        //Jumps to the end of the current fade of the channel and runs its callback
        private void CompleteFade(string channel)
        {
            if (!musicFades.TryGetValue(channel, out MusicFade fade))
                return;

            musicFades.Remove(channel);
            SetFade(channel, fade.to);
            fade.onComplete?.Invoke();
        }

        //Uses unscaled time so fades still work when the game is paused with timeScale = 0
        private void UpdateFades()
        {
            if (musicFades.Count == 0)
                return;

            fadeKeysBuffer.Clear();
            fadeKeysBuffer.AddRange(musicFades.Keys);
            foreach (string channel in fadeKeysBuffer)
            {
                if (!musicFades.TryGetValue(channel, out MusicFade fade))
                    continue;

                fade.elapsed += Time.unscaledDeltaTime;
                if (fade.elapsed >= fade.duration)
                    CompleteFade(channel);
                else
                    SetFade(channel, Mathf.Lerp(fade.from, fade.to, fade.elapsed / fade.duration));
            }
        }

        private void ApplyMusicVolume(string channel)
        {
            AudioSource source = GetMusicChannel(channel);
            if (source != null && channels_music_volume.TryGetValue(channel, out float vol))
                source.volume = FinalMusicVolume(vol) * GetFade(channel) * duckCurrent;
        }

        public void RefreshVolume()
        {
            foreach (KeyValuePair<string, AudioSource> pair in channels_sfx)
            {
                if (pair.Value != null && channels_sfx_volume.TryGetValue(pair.Key, out float vol))
                    pair.Value.volume = FinalSfxVolume(vol);
            }

            foreach (string channel in channels_music.Keys)
                ApplyMusicVolume(channel);

            foreach (AudioSource source in oneShotSources)
            {
                if (source != null)
                    source.volume = FinalSfxVolume(1f);
            }
        }

        public bool IsMusicPlaying(string channel)
        {
            AudioSource source = GetMusicChannel(channel);
            return source != null && source.isPlaying;
        }

        public AudioSource GetChannel(string channel)
        {
            if (!string.IsNullOrEmpty(channel) && channels_sfx.TryGetValue(channel, out AudioSource source))
                return source;
            return null;
        }

        public AudioSource GetMusicChannel(string channel)
        {
            if (!string.IsNullOrEmpty(channel) && channels_music.TryGetValue(channel, out AudioSource source))
                return source;
            return null;
        }

        public bool DoesChannelExist(string channel)
        {
            return GetChannel(channel) != null;
        }

        public bool DoesMusicChannelExist(string channel)
        {
            return GetMusicChannel(channel) != null;
        }

        public AudioSource CreateChannel(string channel, int priority = 128)
        {
            if (string.IsNullOrEmpty(channel))
                return null;

            GameObject cobj = new GameObject("AudioChannel-" + channel);
            cobj.transform.parent = transform;
            AudioSource caudio = cobj.AddComponent<AudioSource>();
            caudio.playOnAwake = false;
            caudio.loop = false;
            caudio.priority = priority;
            return caudio;
        }

        private static void ResumeOrPlay(AudioSource source)
        {
            //UnPause only works on a paused source, a stopped or never played source has time == 0
            if (source.time > 0f)
                source.UnPause();
            else
                source.Play();
        }

        //Shortcuts
        public static void Music(string channel, AudioClip audio, float volume = DEFAULT_MUSIC_VOLUME, float fadeDuration = 0f) { if (instance != null) instance.PlayMusic(channel, audio, volume, fadeDuration: fadeDuration); }
        public static void SFX(string channel, AudioClip audio, float volume = DEFAULT_SFX_VOLUME, bool loop = false, float pitchRandom = 0f) { if (instance != null) instance.PlaySFX(channel, audio, volume, loop: loop, pitchRandom: pitchRandom); }
        public static void SFXOneShot(AudioClip audio, float volume = DEFAULT_SFX_VOLUME, float pitchRandom = 0f) { if (instance != null) instance.PlaySFXOneShot(audio, volume, pitchRandom); }
        public static void SFXWithDuck(string channel, AudioClip audio, float volume = DEFAULT_SFX_VOLUME, float duckVolume = DEFAULT_DUCK_VOLUME) { if (instance != null) instance.PlaySFXWithDuck(channel, audio, volume, duckVolume); }
        public static void SFX(string id) { if (instance != null) instance.PlaySFX(id); } //Sound from the AudioLibrary
        public static void Music(string id, float fadeDuration = 0f) { if (instance != null) instance.PlayMusic(id, fadeDuration); } //Music from the AudioLibrary
        public static void Stop(string channel, float fadeDuration = 0f) { if (instance != null) instance.StopMusic(channel, fadeDuration); } //Stops music
        public static void StopSFX(string channel) { if (instance != null) instance.StopSoundFX(channel); } //Stops sound effect

        public static void ClickButton()
        {
            ClickButton(null);
        }

        //sound: null to use the default clickSound
        public static void ClickButton(AudioClip sound, float volume = DEFAULT_CLICK_VOLUME)
        {
            if (instance == null) return;

            instance.PlaySFX(CLICK_CHANNEL, sound != null ? sound : instance.clickSound, volume);
        }

        //Plays a random track from bgMusics. With more than one track, tracks are chained without repeating the current one
        public static void PlayBGMusic()
        {
            if (instance == null) return;

            instance.PlayNextBGMusic();
        }

        private void PlayNextBGMusic()
        {
            if (bgMusics == null || bgMusics.Length <= 0) return;

            bool playlist = bgMusics.Length > 1;
            int next = Random.Range(0, bgMusics.Length);
            if (playlist && next == bgMusicIndex)
                next = (next + 1) % bgMusics.Length;

            bgMusicIndex = next;
            PlayMusic(BG_MUSIC_CHANNEL, bgMusics[next], DEFAULT_MUSIC_VOLUME, !playlist, bgMusicFadeDuration);
            bgPlaylistActive = playlist; //After PlayMusic, which turns the playlist off
        }

        private void UpdateBGPlaylist()
        {
            if (!bgPlaylistActive || !CanPlayMusic)
                return;
            if (userPausedMusic.Contains(BG_MUSIC_CHANNEL) || suspendedMusic.Contains(BG_MUSIC_CHANNEL))
                return;

            AudioSource source = GetMusicChannel(BG_MUSIC_CHANNEL);
            if (source != null && !source.isPlaying)
                PlayNextBGMusic();
        }

        public void StopAllSound()
        {
            foreach (string channel in new List<string>(channels_music.Keys))
                StopMusic(channel);
            StopAllSoundFX();
        }

        public void StopAllSoundFX()
        {
            foreach (var source in channels_sfx.Values)
            {
                if (source != null)
                    source.Stop();
            }
            foreach (AudioSource source in oneShotSources)
            {
                if (source != null)
                    source.Stop();
            }
            suspendedSfx.Clear();
            suspendedDuckSfx.Clear();
        }

        //Master switch for both music and sound effects. Disabling pauses the music and stops sound effects, enabling resumes the music that was playing
        public void SetEnableSound(bool enable)
        {
            if (isSoundEnabled == enable)
                return;

            isSoundEnabled = enable;
            settingsDirty = true;
            UpdateMusicEnabledState();
            UpdateSfxEnabledState();
        }

        public void SetEnableMusic(bool enable)
        {
            if (isMusicEnabled == enable)
                return;

            isMusicEnabled = enable;
            settingsDirty = true;
            UpdateMusicEnabledState();
        }

        public void SetEnableSfx(bool enable)
        {
            if (isSfxEnabled == enable)
                return;

            isSfxEnabled = enable;
            settingsDirty = true;
            UpdateSfxEnabledState();
        }

        //Disabling stops sound effects but only pauses looping ones (ex: ambience), which are resumed when sound effects are enabled again
        private void UpdateSfxEnabledState()
        {
            if (!CanPlaySfx)
            {
                foreach (KeyValuePair<string, AudioSource> pair in channels_sfx)
                {
                    AudioSource source = pair.Value;
                    if (source == null)
                        continue;

                    if (source.loop && source.isPlaying)
                    {
                        if (duckSources.Contains(source))
                            suspendedDuckSfx[pair.Key] = duckLevel; //Paused sources are dropped from duckSources, the duck is restored on resume
                        source.Pause();
                        suspendedSfx.Add(pair.Key);
                    }
                    else if (!suspendedSfx.Contains(pair.Key))
                    {
                        source.Stop();
                    }
                }
                foreach (AudioSource source in oneShotSources)
                {
                    if (source != null)
                        source.Stop();
                }
            }
            else
            {
                foreach (string channel in suspendedSfx)
                {
                    AudioSource source = GetChannel(channel);
                    if (source == null)
                        continue;

                    source.UnPause();
                    if (suspendedDuckSfx.TryGetValue(channel, out float level))
                        DuckWhilePlaying(source, level);
                }
                suspendedSfx.Clear();
                suspendedDuckSfx.Clear();
            }
        }

        private void UpdateMusicEnabledState()
        {
            if (!CanPlayMusic)
            {
                foreach (string channel in new List<string>(channels_music.Keys))
                {
                    CompleteFade(channel);
                    AudioSource source = GetMusicChannel(channel);
                    if (source != null && source.isPlaying)
                    {
                        source.Pause();
                        suspendedMusic.Add(channel);
                    }
                }
            }
            else
            {
                foreach (string channel in suspendedMusic)
                {
                    AudioSource source = GetMusicChannel(channel);
                    if (source != null && source.clip != null)
                        ResumeOrPlay(source);
                }
                suspendedMusic.Clear();
            }
        }

        public void SetMasterVolume(float volume)
        {
            masterVolume = Mathf.Clamp01(volume);
            settingsDirty = true;
            RefreshVolume();
        }

        public void SetSfxVolume(float volume)
        {
            sfxVolume = Mathf.Clamp01(volume);
            settingsDirty = true;
            RefreshVolume();
        }

        public void SetMusicVolume(float volume)
        {
            musicVolume = Mathf.Clamp01(volume);
            settingsDirty = true;
            RefreshVolume();
        }
    }

}
