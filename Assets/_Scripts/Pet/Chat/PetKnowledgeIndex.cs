using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using GIC.Framework;
using UnityEngine;

namespace GIC.Pet.Chat
{
    /// <summary>
    /// 派蒙知识库检索层（docs/19 §6.5.9）：search_knowledge 工具的执行体，宠物进程本地消化（不经 IPC——
    /// 知识文件随包分发，主游戏没开也能答）。
    /// 架构对齐 Spring AI 的 RAG 组件划分：Entry=Document（id/正文/元数据），本类=DocumentRetriever 的
    /// 词法打分实现——知识规模小（百余条），标题/别名/关键词分层打分即可零误差覆盖，维持 2026-08-28
    /// "不上向量库"拍板；未来若需语义检索，换一个 Retriever 实现（打分函数）即可，条目模型与工具协议不动。
    /// 数据=构建期烘焙的 Assets/StreamingAssets/pet_knowledge.json（PetKnowledgeBaker 产出，
    /// ETL 三段：UnitConfig+本地化表+FAQ 源 → 规整（参数值代入/富文本剥离）→ 落盘）——桌宠独立进程
    /// 不加载游戏配置/Addressables，两形态共用这份静态知识文件（StreamingAssets 随任意 Player 构建打包）。
    /// </summary>
    public static class PetKnowledgeIndex
    {
        // ---------- Document 模型（与烘焙器 JSON 字段一一对应） ----------

        [Serializable]
        public class Entry
        {
            public string id;
            public string category;   // unit=角色 | faq=玩法规则与说明
            public string title;
            public string[] aliases;   // 英文名/称号/俗称（等价命中）
            public string[] keywords;  // 元素/武器/技能名等（弱命中，用于泛查）
            public string body;        // 面向 LLM 的正文（参数值已代入、TMP 富文本已剥离）
        }

        [Serializable]
        private class KnowledgeFile
        {
            public int version;
            public string bakedAt;
            public Entry[] entries;
        }

        // ---------- 检索结果（喂给 LLM 的 role:"tool" 回执体） ----------

        [Serializable]
        public class Result
        {
            public string id;
            public string category;
            public string title;
            public string body;
        }

        private const string FileName = "pet_knowledge.json";
        private const int MaxResults = 5;

        private static Entry[] _entries;
        private static bool _loaded;

        /// <summary>检索（词法打分：等价命中 > 包含命中、短字段 > 长字段；多词各自命中取分求和取 top N）。
        /// 返回 JSON 字符串作 role:"tool" 回执；无命中也算成功（count=0，LLM 按人设坦白说不知道）。</summary>
        public static string Search(string query, string category = null)
        {
            if (!TryLoad(out string loadError))
                return JsonConvert.SerializeObject(new { ok = false, error = loadError });

            // 分词：空格分隔；去重防叠分（"凯亚 凯亚"不应双倍计分）
            var tokens = (query ?? "")
                .Split((char[])null, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().ToLowerInvariant())
                .Where(t => t.Length > 0)
                .Distinct()
                .ToArray();
            if (tokens.Length == 0)
                return JsonConvert.SerializeObject(new { ok = false, error = "query 为空" });

            var hits = new List<(int score, Entry e)>();
            foreach (var e in _entries)
            {
                if (!string.IsNullOrEmpty(category) &&
                    !string.Equals(e.category, category, StringComparison.OrdinalIgnoreCase)) continue;

                string titleLower = e.title?.ToLowerInvariant() ?? "";
                int score = 0;
                foreach (var token in tokens)
                {
                    int s = ScoreToken(e, titleLower, token);
                    if (s > 0) score += s;
                }
                if (score > 0) hits.Add((score, e));
            }

            if (hits.Count == 0)
                return JsonConvert.SerializeObject(new { ok = true, count = 0, message = "知识库里没查到相关内容" });

            var results = hits.OrderByDescending(h => h.score).Take(MaxResults)
                .Select(h => new Result { id = h.e.id, category = h.e.category, title = h.e.title, body = h.e.body })
                .ToArray();
            return JsonConvert.SerializeObject(new { ok = true, count = results.Length, results });
        }

        /// <summary>单 token 分层打分（对齐主流检索框架的分层命中：等价 > 包含、短字段 > 长字段；
        /// 包含命中要求 token≥2 字，防单字在长正文里滥命中——单字（如"冰"）仍可等价命中关键词）</summary>
        static int ScoreToken(Entry e, string titleLower, string token)
        {
            // 等价命中
            if (titleLower == token) return 100;
            if (e.aliases != null)
                foreach (var a in e.aliases)
                    if ((a ?? "").ToLowerInvariant() == token) return 90;
            if (e.keywords != null)
                foreach (var k in e.keywords)
                    if ((k ?? "").ToLowerInvariant() == token) return 30;

            // 包含命中
            if (token.Length < 2) return 0;
            if (titleLower.Contains(token)) return 60;
            if (e.aliases != null)
                foreach (var a in e.aliases)
                    if ((a ?? "").ToLowerInvariant().Contains(token)) return 40;
            if (e.keywords != null)
                foreach (var k in e.keywords)
                    if ((k ?? "").ToLowerInvariant().Contains(token)) return 25;
            if ((e.body ?? "").ToLowerInvariant().Contains(token)) return 10;
            return 0;
        }

        // ---------- 加载（StreamingAssets 直读：Windows 桌宠/编辑器/主游戏均可；
        // Android 主游戏 jar 内路径读不到=优雅报错不崩溃） ----------

        static bool TryLoad(out string error)
        {
            error = null;
            if (_loaded) return _entries != null;
            _loaded = true;
            try
            {
                string path = Path.Combine(Application.streamingAssetsPath, FileName);
                if (!File.Exists(path))
                {
                    error = "知识库文件不存在（需先运行 Tools/桌宠/烘焙派蒙知识库）";
                    return false;
                }
                var file = JsonConvert.DeserializeObject<KnowledgeFile>(File.ReadAllText(path));
                _entries = file?.entries ?? new Entry[0];
                GICLog.DevInfo($"[PetChat] 知识库已加载：{_entries.Length} 条（bakedAt={file?.bakedAt}）");
                return true;
            }
            catch (Exception e)
            {
                error = $"知识库加载失败: {e.Message}";
                _entries = null;
                return false;
            }
        }

        /// <summary>条目总数（诊断用；未加载返回 -1）</summary>
        public static int EntryCount => _entries?.Length ?? -1;
    }
}
