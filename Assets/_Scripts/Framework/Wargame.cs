using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
namespace GIC.Framework
{


    public class Wargame : Singleton<Wargame>, IWargameManager
    {
        public ApplicationContext Context { get; private set; }

        // 兼容旧调用：通过 Context 获取
        public ConfigManager ConfigManager => Context?.Get<ConfigManager>();
        public SaveManager SaveManager => Context?.Get<SaveManager>();
        public InputManager InputManager => Context?.Get<InputManager>();
        public UIManager UIManager => Context?.Get<UIManager>();
        public CardManager CardManager => Context?.Get<CardManager>();
        public PositionManager PositionManager => Context?.Get<PositionManager>();
        public PlayerManager PlayerManager => Context?.Get<PlayerManager>();
        public SkillManager SkillManager => Context?.Get<SkillManager>();
        public UnitManager UnitManager => Context?.Get<UnitManager>();

        List<IWargameManager> managers;

        public override void Init()
        {
            Context = new ApplicationContext();

            // ── 阶段1：注册 [Configuration] 类（无构造函数依赖）──
            Context.Register<ConfigManager>();

            // 处理 [Configuration]：调用 [Bean] 方法产出 configs（等价 Spring refresh 的 invokeBeanFactoryPostProcessors）
            Context.ProcessConfigurations();

            // ── 阶段2：按依赖顺序注册 [Component] 类（构造器注入，依赖必须已就绪）──
            Context.Register<SaveManager>();       // deps: UnitConfig, ItemConfig
            Context.Register<CardManager>();       // deps: SaveManager, ItemConfig, UnitConfig
            Context.Register<PositionManager>();   // deps: SaveManager, PositionConfig
            Context.Register<PlayerManager>();     // deps: SaveManager
            Context.Register<UnitManager>();       // deps: UnitConfig
            Context.Register<InputManager>();
            Context.Register<UIManager>();
            Context.Register<SkillManager>();

            // 注入剩余 [Autowired] 字段（ConfigManager 自身 [Bean] 产物、MonoBehaviour 层）
            Context.InjectAll();

            // 校验所有依赖是否注入成功，缺失则抛异常快速失败
            Context.Validate();

            // 构建 managers 列表（用于 Update 循环）
            managers = new List<IWargameManager> { ConfigManager, SaveManager, InputManager, UIManager, CardManager, PositionManager, PlayerManager, SkillManager, UnitManager };

            Debug.Log("Wargame初始化完成");
            Debug.Log(Application.consoleLogPath);

        }

        public void Start()
        {
            // 调用所有 [PostConstruct] 方法（等价 Spring finishBeanFactoryInitialization → @PostConstruct）
            Context.PostConstruct();
        }

        public void Update(float deltaTime)
        {
            if (managers == null) return;
            foreach (var manager in managers)
            {
                manager?.Update(deltaTime);
            }
        }
    }

}
