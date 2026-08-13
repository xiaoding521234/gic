using System;

namespace GIC.Framework
{
    /// <summary>
    /// 标记类为容器管理的单例 Bean（等价 Spring @Component）
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ComponentAttribute : Attribute { }

    /// <summary>
    /// 字段注入（等价 Spring @Autowired）
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public class AutowiredAttribute : Attribute { }

    /// <summary>
    /// 配置类（等价 Spring @Configuration），其 [Bean] 方法产出的对象注册为 Bean
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class ConfigurationAttribute : Attribute { }

    /// <summary>
    /// 方法产出 Bean（等价 Spring @Bean），仅 [Configuration] 类中生效
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class BeanAttribute : Attribute { }

    /// <summary>
    /// 注入完成后调用（等价 Spring @PostConstruct），方法须无参无返回值
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class PostConstructAttribute : Attribute { }
}
