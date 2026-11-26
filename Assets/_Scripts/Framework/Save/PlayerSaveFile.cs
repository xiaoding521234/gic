using System;
using System.IO;
using System.Text;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>玩家存档的文件边界：区分缺档、坏档、读失败与版本不兼容，集中处理原子替换。</summary>
    public sealed class PlayerSaveFile
    {
        public enum State { Missing, Valid, Invalid, Older, Newer, Unreadable }

        [Serializable]
        private sealed class VersionHeader { public int saveVersion = 0; }

        private readonly string _path;
        private readonly int _currentVersion;
        private string BackupPath => _path + ".backup";
        private string TempPath => _path + ".tmp";

        public PlayerSaveFile(string path, int currentVersion)
        {
            _path = Path.GetFullPath(path);
            _currentVersion = currentVersion;
        }

        public static bool ProtectsExistingFile(State state) => state == State.Newer || state == State.Unreadable;

        public State Read(bool backup, out PlayerSaveData data) => Read(backup, out data, versionOnly: false);

        private State Read(bool backup, out PlayerSaveData data, bool versionOnly)
        {
            data = null;
            var path = backup ? BackupPath : _path;
            string json;
            try { json = File.ReadAllText(path, Encoding.UTF8); }
            catch (FileNotFoundException) { return State.Missing; }
            catch (DirectoryNotFoundException) { return State.Missing; }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
                GICLog.Warn($"无法读取存档 {path}：{e.Message}");
                return State.Unreadable;
            }

            try
            {
                int version = JsonUtility.FromJson<VersionHeader>(json)?.saveVersion ?? 0;
                if (version <= 0) return State.Invalid;
                if (version > _currentVersion) return State.Newer;
                if (version < _currentVersion) return State.Older;
                if (versionOnly) return State.Valid;
                data = JsonUtility.FromJson<PlayerSaveData>(json);
                return data == null || data.saveVersion != version ? State.Invalid : State.Valid;
            }
            catch (ArgumentException e)
            {
                GICLog.Warn($"无法解析存档 {path}：{e.Message}");
                return State.Invalid;
            }
        }

        /// <summary>只把已经确认可用的旧主档滚入备份。恢复备份时不得将坏主档滚回备份位。</summary>
        public bool TryWrite(string json, bool rotateBackup)
        {
            var main = Read(false, out _, versionOnly: !rotateBackup);
            if (ProtectsExistingFile(main)
                || ProtectsExistingFile(Read(true, out _, versionOnly: true))) return false;
            Directory.CreateDirectory(Path.GetDirectoryName(_path));
            File.WriteAllText(TempPath, json, Encoding.UTF8);
            if (main != State.Missing)
                File.Replace(TempPath, _path, rotateBackup && main == State.Valid ? BackupPath : null);
            else File.Move(TempPath, _path);
            return true;
        }

        public void DeleteForTesting()
        {
            File.Delete(_path);
            File.Delete(BackupPath);
            File.Delete(TempPath);
        }
    }
}
