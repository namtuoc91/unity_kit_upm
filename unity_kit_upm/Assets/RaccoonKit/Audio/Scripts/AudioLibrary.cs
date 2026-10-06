using System;
using System.Collections.Generic;
using UnityEngine;

namespace Raccoon.Audio
{
    [Serializable]
    public class SoundEntry
    {
        public string id;
        [Tooltip("A random clip is picked each time the sound is played")]
        public AudioClip[] clips;
        [Range(0f, 1f)] public float volume = GameAudio.DEFAULT_SFX_VOLUME;
        [Range(0f, 0.5f)] public float pitchRandom;
        [Tooltip("Empty: played as one shot, sounds overlap. Set: played on this channel, a new sound cuts the previous one")]
        public string channel;
        [Tooltip("Only for sounds with a channel, one shots can't loop")]
        public bool loop;
        [Tooltip("Lowers the music while this sound is playing")]
        public bool duckMusic;

        [SerializeField, HideInInspector] private bool initialized;

        //Unity may add new list elements with zeroed fields instead of the initializers above, which would make the sound silent
        public void InitDefaults()
        {
            if (initialized)
                return;
            if (volume <= 0f) //Zeroed element, entries already set up keep their values
                volume = GameAudio.DEFAULT_SFX_VOLUME;
            initialized = true;
        }

        public AudioClip GetRandomClip()
        {
            if (clips == null || clips.Length == 0)
                return null;
            return clips[UnityEngine.Random.Range(0, clips.Length)];
        }
    }

    [Serializable]
    public class MusicEntry
    {
        public string id;
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = GameAudio.DEFAULT_MUSIC_VOLUME;
        [Tooltip("Empty: played on " + GameAudio.BG_MUSIC_CHANNEL)]
        public string channel;
        public bool loop = true;

        [SerializeField, HideInInspector] private bool initialized;

        //Unity may add new list elements with zeroed fields instead of the initializers above, which would make the music silent
        public void InitDefaults()
        {
            if (initialized)
                return;
            if (volume <= 0f) //Zeroed element, entries already set up keep their values
            {
                volume = GameAudio.DEFAULT_MUSIC_VOLUME;
                loop = true;
            }
            initialized = true;
        }
    }

    /// <summary>
    /// List of sounds and musics called by id with GameAudio.SFX("id") / GameAudio.Music("id"), so clips don't need to be referenced everywhere
    /// </summary>

    [CreateAssetMenu(fileName = "AudioLibrary", menuName = "Raccoon/Audio Library")]
    public class AudioLibrary : ScriptableObject
    {
        [SerializeField] private List<SoundEntry> sounds = new List<SoundEntry>();
        [SerializeField] private List<MusicEntry> musics = new List<MusicEntry>();

        private Dictionary<string, SoundEntry> soundMap;
        private Dictionary<string, MusicEntry> musicMap;

        private void OnEnable()
        {
            soundMap = null;
            musicMap = null;
        }

        private void OnValidate()
        {
            soundMap = null;
            musicMap = null;

            //New elements get their defaults once, duplicated elements keep the copied values
            foreach (SoundEntry entry in sounds)
                entry?.InitDefaults();
            foreach (MusicEntry entry in musics)
                entry?.InitDefaults();
        }

        public bool TryGetSound(string id, out SoundEntry entry)
        {
            if (soundMap == null)
                soundMap = BuildMap(sounds, e => e.id);
            entry = null;
            return !string.IsNullOrEmpty(id) && soundMap.TryGetValue(id, out entry);
        }

        public bool TryGetMusic(string id, out MusicEntry entry)
        {
            if (musicMap == null)
                musicMap = BuildMap(musics, e => e.id);
            entry = null;
            return !string.IsNullOrEmpty(id) && musicMap.TryGetValue(id, out entry);
        }

        private Dictionary<string, T> BuildMap<T>(List<T> list, Func<T, string> getId)
        {
            var map = new Dictionary<string, T>();
            foreach (T entry in list)
            {
                string id = entry != null ? getId(entry) : null;
                if (string.IsNullOrEmpty(id))
                    continue;
                if (map.ContainsKey(id))
                    Debug.LogWarning($"[AudioLibrary] Duplicate id '{id}' in {name}, only the first one is used", this);
                else
                    map[id] = entry;
            }
            return map;
        }
    }

}
