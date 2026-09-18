# 派蒙语音 TTS — 音色生产与侧车运维

> 2026-09-17 建立。**分工**：[19-AI派蒙.md §6.5.11](19-AI派蒙.md) 记游戏侧语音系统的设计与实现（三模式/组件/设置项），本文档记**游戏之外**的部分——音色模型谱系、GPT-SoVITS 训练基建、数据集、评测纪律与侧车运维。侧车目录 `D:\Tool\GPT-SoVITS\` 不在项目仓库内，全档落此处。

## 1. 设计总纲（一句话版）

**零进包**：游戏构建不塞任何 TTS 引擎/模型；语音=玩家自配进阶功能（本地侧车 / 云端 key / 关闭，三模式详见 19 §6.5.11）。本地侧车=GPT-SoVITS api_v2（127.0.0.1:9880）；官方派蒙音色不存在开源模型，音色靠社区模型起步 + 自训升级。

## 2. 侧车部署（D:\Tool\GPT-SoVITS\）

### 2.1 目录布局

| 目录 | 内容 |
|------|------|
| `repo\` | RVC-Boss/GPT-SoVITS clone（训练+推理代码）；预训练在 `repo\GPT_SoVITS\pretrained_models\`（gsv-v2final s1 / gsv-v4 s2Gv4 / chinese-hubert-base / chinese-roberta-wwm-ext-large，hf-mirror.com/lj1995/GPT-SoVITS 下载——HF 目录在仓库顶层非 pretrained_models/ 下） |
| `venv\` | Python 3.10.11 独立装于 `D:\Tool\Python310`（不动系统 PATH），venv 内 CUDA torch 2.5.1+cu121 |
| `paimon\` | 音色资产与侧车配置：pm-v2-epoch40.pth、ref_pm.wav（6.5s/48kHz 默认参考音频）、各代 yaml、合成/评测脚本、compare\ 对比文件 |
| `paimon_ds\` | 训练数据与驱动：数据集 parquet/jsonl、`paimon_full.list`、`train_wavs\`、`train_pm_v4.py`、check_wavs.py 等 |
| `start_paimon_tts.bat` | 侧车启动：`api_v2.py -a 127.0.0.1 -p 9880 -c <yaml>` |

### 2.2 环境版本铁律（装坏 = 全链炸）

- **transformers 必须 4.51.3**：4.52+ 有 torch.load 安全门禁，直接炸加载。
- **pyopenjtalk 装不上就跳过**（代码有 try 守卫）；**jieba_fast** 用纯 jieba + 包垫片（`site-packages/jieba_fast/__init__.py` 里 `sys.modules['jieba_fast.posseg']=jieba.posseg`）。
- **ffmpeg 不在 PATH**：训练预处理经 ffmpeg-python 调二进制，环境需注入 `D:\Tool\FormatFactory`（train 脚本已内置）。

### 2.3 API 契约（api_v2）

`POST /tts` JSON：`text / text_lang / ref_audio_path（每请求必填）/ prompt_text（每请求必填）/ prompt_lang / top_k 20 / top_p 0.85 / temp 0.75 / text_split_method cut0 / media_type wav / streaming_mode false`；输出 WAV 32kHz 16bit 单声道。参考音频决定语气，GPT-SoVITS 对参考文本极敏感。

另有 `POST /set_sovits_weights` 热切换端点——换模型档无需杀进程（2026-09-17 实证）。

### 2.4 ⚠️ yaml 铁律：模型路径必须绝对路径

侧车 yaml（`tts_infer_paimon*.yaml`）custom 段的 `vits_weights_path` 等**必须写绝对路径**。相对路径（`GPT_SoVITS/...`）api_v2 解析失败后**静默回退底模** `gsv-v4-pretrained/s2Gv4.pth`，无任何报错——2026-09-17 六路对比文件的 v4full 列整列实为底模声（声纹溯源 check_sixway_src.py cos 判别 + 加载日志双实锤），根源即此。修法=per-epoch yaml 全部绝对路径重生成。

### 2.5 侧车运维（换档操作）

- 常规启动：跑 `start_paimon_tts.bat`（换档=改 bat 引用的 yaml）。
- 热切换：`POST /set_sovits_weights {"sovits_weights_path": "<绝对路径 pth>"}`。
- 现存 yaml 清单（`paimon\`）：`tts_infer_paimon.yaml`（v2 社区模型）/ `_v4.yaml` / `_v4e12.yaml` / `_v4e16.yaml`（pm_v4 抽样轮）/ `_v4full.yaml`（⚠️ 相对路径坏例，仅留档）/ `_v4full_e{2,4,6,8}.yaml`（第一轮全量 per-epoch，已修绝对路径）。
- 游戏侧接线：pet.json `voiceSidecarRefPath/RefText` 默认指向 `D:\Tool\GPT-SoVITS\paimon\`（玩家自建侧车自己改）。

## 3. 音色模型谱系

| 代际 | 权重 | 数据 | 状态与耳检结论 |
|------|------|------|----------------|
| v2 社区 | `paimon\pm-v2-epoch40.pth`（162MB，S2 微调全量格式） | 社区 60 分钟派蒙数据（[xianglun918/paimon-tts](https://github.com/xianglun918/paimon-tts) Releases，个人研究非商用，sha256 已验） | 起步模型。耳检"**接近派蒙但听得出不是派蒙**"，用户验收通过当前等级 |
| v4 抽样轮 `pm_v4` | `repo\SoVITS_weights_v4\pm_v4_e12_s5040_l32.pth` / `pm_v4_e16_s6720_l32.pth`（75MB lora 格式） | 2,500 条抽样 | e8 断点实验后续训。耳检 e12/e16 不相上下；九句里 8 句达不到官方强度；emoref 实验（炸毛参考）"立起来了但过头"→**语气=参考音频传导机制证实**（平稳参考→偏软；炸毛参考→过头） |
| v4 全量一轮 `pm_v4full` | `pm_v4full_e{2,4,6,8}_s*_l32.pth`（各 72.1MB） | 6,670 条全量 × 8 epoch（总条次 5.3 万） | 四档权重落盘；六路对比文件事故（v4full 列=底模声）修复后已重合成真权重版 |
| v4 全量二轮 `pm_v4fullc`（**已停止**） | `pm_v4fullc_e2_s2230_l32.pth`（仅 e2 落盘） | 同上，干净数据复核 + 独占内存重跑 | 2026-09-17 22:29 发射，**23:14 用户拍板停止（先采用 §9 A 组零训练优化）**——epoch 2 完成时终止，e2 保留；如需重启 `train_pm_v4.py --skip-prep`（头常量已指 pm_v4fullc，预处理产物齐备） |

**关键机制结论（耳检存档）**：输出语气主要由参考音频传导——想要某情绪的输出，用同情绪的参考音频；分情绪参考表已建（§9 A1，2026-09-17）；中间档候选 `vo_MDAQ046_1_paimon_02`"哼，感觉你的比喻很失礼啊"已入 sneer 档系统化重测（此前 sampling 轮 `gen_refdegrees.py` 的 midref/mixref 实验只测过 8/9 两句、结论未落档）。评估器与耳朵矛盾时**人耳终审**。

**弃用资产**：旧 gichess mod 提取物全面禁用（2026-09-16 用户拍板），含 4.4s 播报员 wav 曾试作克隆样本——不再作为任何 GIC 素材来源。

## 4. 数据集

- **来源**：DataSpeech 处理的原神 4.8 中文派蒙语料（`paimon_ds\dataspeech_Genshin4.8_CN_paimon*.parquet`）→ 解析为 `paimon_rows.jsonl` → join 音频 → `paimon_full.list`（6,670 条 join 全命中）+ `train_wavs\`。
- **抽样开关**：`build_final_list.py` 已禁抽样直出全量——注意第一轮曾因编辑未生效（replace 被取消）导致 `INP_TEXT` 指向不存在文件、1a 预处理 FileNotFoundError；改常量后必须核实落盘内容。
- **坏档排查法**：`check_wavs.py` 全量预检（6,670 条 0 坏档，2026-09-17 复核）。训练中段 ffmpeg error 崩溃 ≠ 文件损坏——是内存耗尽的子进程分配失败，先查内存再怀疑数据。

## 5. 训练驱动 train_pm_v4.py

- 用法：repo 根目录以 venv python 运行 `D:\Tool\GPT-SoVITS\paimon_ds\train_pm_v4.py`；**实验参数在头部常量改**（`EXP_NAME` / `INP_TEXT` / `EPOCHS` / `SAVE_EVERY` 2 / `LORA_RANK` 32 / BATCH 6）。
- `--skip-prep` 断点续训：预处理产物（`logs\{EXP}\` 下 2-name2text / 6-name2semantic / 4-cnhubert / 5-wav32k）已备齐时直跳训练。新实验可从旧实验复制预处理产物再 skip-prep。
- 产出：`repo\SoVITS_weights_v4\{EXP}_e{N}_s{steps}_l32.pth`（每 2 epoch 存档，纯 lora 格式 ~72-75MB）。手动全量转换（`convert_e8.py`）会出 ~780MB 全量格式，两种格式推理侧都能加载。
- **转换坑**：savee 接口喂 SimpleNamespace 会炸加载——config 必须纯 dict。

## 6. 评测与耳检流程

| 工具（`paimon\`） | 用途 |
|------|------|
| `gen_lines.py <prefix>` | 九句固定台词批量合成（对比素材）；`gen_emoref.py` / `gen_refdegrees.py` 参考音频实验 |
| `make_compare_full.py` | 拼对比大文件：`compare\paimon_compare_full4_off_e2_e4_e6_e8.wav`（官方+四档主对比，23.7MB）、`paimon_compare_sixway_fixed.wav`（六路修复版，28MB） |
| `eval_compare.py cpu` | 客观评测：eres2net 声纹余弦 + RMS/F0P95 强度比 + funasr 字错率。**训练期间勿跑 GPU 版**；`eval_full.py` 可随时重跑四档（结果 `eval_scores_full.json`） |
| `check_sixway_src.py` | 声纹溯源（判别某列音频到底加载的哪个权重） |

**指标只可参考，人耳终审**——首跑三矛盾实证：emoref RMS 反而更低（"过头"体现在频率不在响度）、cer 0.84 虚高（标点计入）、指标排序与耳检排序不一致。

**耳检流程定档**：四档权重各配 per-epoch yaml（绝对路径）→ `gen_lines.py` 合成九句 → 拼/更新对比文件 → 用户耳检拍板 → 侧车 yaml 定稿。

## 7. 训练纪律（16GB 笔记本血泪）

1. **训练 = 独占任务**：e8 抽样轮曾在 epoch 10 内存耗尽 0xC0000409 机器卡死（训练 RSS 爬升 + 侧车 + Unity + 检查点探查叠加）。
2. **三不**：不碰检查点文件、不起侧车、不跑并行重活（含 GPU 评测）；Unity 编辑器建议关。
3. 训练进程脱离 CLI 会话独立运行（Start-Process 脱管），跨会话接手先查：进程存活（`train_pm_v4.py`）→ `train_fullc.log` 尾部步数 → `SoVITS_weights_v4\` 检查点落盘情况。日志：`paimon_ds\train_fullc.log` + `.err.log`。
4. 训练完成后侧车才允许拉起。

## 8. 待办

- [ ] **A 组耳检拍板（§9）**：参考表哪些情绪档采纳 / 网格参数是否换 / n-best 是否产品化为游戏侧重试守卫
- [ ] 分情绪参考表游戏侧接线（do_action key → `paimon\refs_table.json` 每句改发对应 ref——拍板后做，含 PetTtsClient 请求参数化）
- [ ] 桌宠测试包重建（桌面形态语音需新构建；PetVoiceSpeaker 清洗器改动后亦需新包）
- [ ] 云端模式（MiniMax T2A v2 + 克隆）无 key 未实测
- [ ] （B 组备选，A 组效果不足时再启）**s1 GPT 半边补训**——`train_pm_v4.py` 只训 s2（`s2_train_v3_lora.py`），s1 从未用派蒙数据微调（`repo\GPT_SoVITS\weights` 为空，推理时 t2s 一直跑底模 s1v3.ckpt，2026-09-17 核实）；韵律/节奏的「派蒙味」归 s1 管
- [ ] （B 组备选）LoRA rank 32 → 升 rank/全量微调（v2 社区模型=全量格式、音色等级不低，rank 32 可能限贴合上限；16GB 显存吃紧需先估）

## 9. A 组零训练优化（2026-09-17 23:14 拍板「先采用A，停止训练」→ 当晚全量交付）

背景：v4fullc 二轮按用户指令停止（见 §3 行）；A 组四件套 + A5 当晚在 pm_v4full **e8** 真权重侧车上全部完成（侧车=Start-Process 脱管拉起 api_v2 + `tts_infer_paimon_v4full_e8.yaml`，日志 `paimon\sidecar_e8.log`，端口 9880）。

### A1/A2 分情绪参考表（do_action 11 键 → 参考档）
- 链路：`select_refs.py`（6,670 条扫时长+转写长度过滤，11 情绪键关键词分桶，候选池 `compare\refs_candidates.json`）→ 人工拍板每桶 1 条 → **`paimon\refs_table.json`**（游戏侧接线数据源；default=ref_pm.wav 平静兜底）。
- 探针合成 `gen_reftable.py`：每情绪 d（默认参考）/e（情绪参考）双版 22 段 → `compare\paimon_compare_reftable.wav`（听序 greet→clap→show→shy→anger→confuse→nod→shake_head→sneer→hope→refuse，每情绪 d→e）。
- 客观（`rt_scores.json`）：d/e 声纹 sim 差 ±0.02（换参考改语气、不改音色本体）；e 版能量比可见偏移（show/anger/hope rms 比 0.63~0.84）；`rt_clap_e` 重复音节 cer 0.107=单 take 坏样实证。**采纳与否待人耳**。

### A3 参数网格（8 配置 × 4 代表句，`grid_scores.json`）
- rank（=sim−2·cer 均值）：p95 0.770 ≥ **base 0.768** > t90 0.766 > p75 0.761 > k50 0.758 > k5 0.756 > t60 0.746 > cut5 0.736 垫底。
- **结论：现役参数维持不动**（p95 差 0.002 在 take 级噪声内，单 take 噪声 ±0.02~0.08）；cut5 长句切段伤连贯性禁用；line8 全配置 cer 0.133=「唔…」的 ASR 转写歧义（我/呃/嗯/啊/哎），非配置问题。耳检文件 `paimon_compare_grid.wav`（4 句 × base→p95→t90）。

### A4 n-best（9 句 × 5 take，`nbest_scores.json`）
- take 级 sim 落差 0.02~0.08；坏 take 实证：`line1_t1` 幻听前缀「哼」（cer 0.1）、`line9_t3` 哼→嘿+sim 掉 0.07、`line6_t3` 万文→问问。
- 每句最优：1=t3、2=t5、3=t3、4=t1、5=t1、6=t1、7=t2、8=t4、9=t2 → `paimon_compare_nbest.wav`（每句：旧基线 v4full_e8_lineN → 最优 take）。
- **产品化建议（待拍板）**：live 聊天不盲目多重试（每 take 1-2s GPU 延迟）；可行守卫=时长健全性校验（合成时长 vs 字数估算，离谱重试一次）+ 高频固定台词离线 n-best 固化为音频资产。

### A5 文本清洗（游戏侧已落码，编译 0 错）
- 核实：`PetVoiceSpeaker` 切句后原样直送 TTS、无任何清洗 → 已补 `SanitizeForSpeech`（白名单：字母数字/CJK/CJK 标点/全角标点/弯引号/省略号破折号；剔除 emoji 代理对/markdown 记号/控制符；空白折叠），`PumpText` 出队单点生效、清洗后空句跳下一句——侧车/云端两模式同链路受益。
- 编译验证：编辑器启动编译 CompileScripts 4707ms、`rg "error CS"` 零命中（2026-09-17 23:31）。

### 工具沉淀（`paimon\`，全部 manifest 驱动）
`select_refs.py`（参考池）→ `gen_reftable.py` / `gen_grid.py` / `gen_nbest.py`（三合成器）→ `score_takes.py`（通用客观评分：eres2net sim + librosa 强度 + funasr CER——Levenshtein 去标点，修正 `eval_compare.py` 集合式 cer 虚高）→ `make_compare_a.py`（对比文件装配，静音件缺失自愈）。
