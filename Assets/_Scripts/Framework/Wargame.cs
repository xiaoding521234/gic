using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class Wargame : Singleton<Wargame>, IWargameManager
{
    public ConfigManager ConfigManager;
    public SaveManager SaveManager;
    public InputManager InputManager;
    public UIManager UIManager;
    public CardManager CardManager;
    public PositionManager PositionManager;
    public PlayerManager PlayerManager;

    public SkillManager SkillManager;
    public UnitManager UnitManager;

    List<IWargameManager> managers;
    
    

    public override void Init()
    {

        ConfigManager = new ConfigManager();
        ConfigManager.SetInstance(ConfigManager);
        SaveManager = new SaveManager();
        InputManager = new InputManager();
        UIManager = new UIManager();
        CardManager = new CardManager();
        PositionManager = new PositionManager();
        PlayerManager = new PlayerManager();
        SkillManager = new SkillManager();
        UnitManager = new UnitManager();

        managers = new List<IWargameManager>();

        managers.Add(ConfigManager);
        managers.Add(SaveManager);
        managers.Add(InputManager);
        managers.Add(UIManager);
        managers.Add(CardManager);  
        managers.Add(PositionManager);
        managers.Add(PlayerManager);
        managers.Add(SkillManager);
        managers.Add(UnitManager);

        Debug.Log("Wargame初始化完成");
        Debug.Log(Application.consoleLogPath);

    }
    
    public void Start()
    {
        managers.ForEach(manager => manager.Start());
        
    }
    


    public void Update(float deltaTime)
    {
        managers.ForEach(manager => manager.Update(deltaTime));
        
    }
}
