using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace GIC.Framework
{
    /// <summary>
    /// IoC 容器 — 高度模仿 Spring ApplicationContext
    /// 流程：Register → ProcessConfigurations → InjectAll → PostConstruct → Validate
    /// </summary>
    public class ApplicationContext
    {
        private readonly Dictionary<Type, object> _beans = new();
        private readonly List<object> _instances = new();

        /// <summary>
        /// 注册类型（等价 Component Scan 发现 @Component，自动实例化）
        /// 支持构造器注入：选择参数最多的 public 构造函数，从容器解析依赖。
        /// 无 public 构造函数时回退到 Activator.CreateInstance。
        /// </summary>
        public void Register<T>() where T : class
        {
            if (_beans.ContainsKey(typeof(T))) return;
            var type = typeof(T);
            var instance = CreateInstance(type);
            _beans[typeof(T)] = instance;
            _instances.Add(instance);
        }

        /// <summary>
        /// 通过反射创建实例，选择参数最多的构造函数并从容器解析依赖
        /// </summary>
        private object CreateInstance(Type type)
        {
            var ctors = type.GetConstructors();
            if (ctors.Length == 0)
                return Activator.CreateInstance(type);

            // Spring 风格：选择参数最多的构造函数
            var ctor = ctors.OrderByDescending(c => c.GetParameters().Length).First();
            var parameters = ctor.GetParameters();

            if (parameters.Length == 0)
                return Activator.CreateInstance(type);

            var args = new object[parameters.Length];
            for (int i = 0; i < parameters.Length; i++)
            {
                var paramType = parameters[i].ParameterType;
                if (!_beans.TryGetValue(paramType, out var dep))
                    throw new InvalidOperationException(
                        $"[ApplicationContext] 无法构造 {type.Name}：" +
                        $"依赖 {paramType.Name} 未注册，请检查注册顺序");
                args[i] = dep;
            }
            return ctor.Invoke(args);
        }

        /// <summary>
        /// 注册已有实例（等价 @Bean 方法返回值放入容器）
        /// </summary>
        public void Register<T>(T instance) where T : class
        {
            var type = typeof(T);
            _beans[type] = instance;
            if (!_instances.Contains(instance))
                _instances.Add(instance);
        }

        /// <summary>
        /// 按类型获取 Bean（等价 getBean(Class)）
        /// </summary>
        public T Get<T>() where T : class
        {
            return _beans.TryGetValue(typeof(T), out var obj) ? obj as T : null;
        }

        /// <summary>
        /// 注入 [Autowired] 字段到任意对象。
        /// 沿继承链逐层扫描（.NET 反射 GetFields 不返回基类的 private 字段，
        /// 基类声明的 [Autowired] 字段必须逐级取 DeclaredOnly 才能命中）。
        /// </summary>
        public void Inject(object instance)
        {
            foreach (var field in GetAutowiredFields(instance.GetType()))
            {
                if (_beans.TryGetValue(field.FieldType, out var dependency))
                    field.SetValue(instance, dependency);
            }
        }

        /// <summary>收集类型及其全部基类上标记 [Autowired] 的字段（含 private）</summary>
        private static IEnumerable<FieldInfo> GetAutowiredFields(Type type)
        {
            for (var t = type; t != null && t != typeof(object); t = t.BaseType)
            {
                var fields = t.GetFields(BindingFlags.NonPublic | BindingFlags.Public |
                                         BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var f in fields)
                {
                    if (f.GetCustomAttribute<AutowiredAttribute>() != null)
                        yield return f;
                }
            }
        }

        /// <summary>
        /// 处理 [Configuration] 类：按依赖顺序调用 [Bean] 方法，检测循环依赖。
        /// 等价 Spring refresh 的 invokeBeanFactoryPostProcessors。
        /// </summary>
        public void ProcessConfigurations()
        {
            // 1. 收集所有 [Configuration] 实例
            var configs = _instances
                .Where(i => i.GetType().GetCustomAttribute<ConfigurationAttribute>() != null)
                .ToList();

            if (configs.Count == 0) return;

            // 2. 构建 产出类型 → Configuration 实例 映射
            var producerMap = new Dictionary<Type, object>();
            foreach (var config in configs)
            {
                var methods = config.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<BeanAttribute>() != null);

                foreach (var method in methods)
                {
                    if (method.ReturnType != typeof(void))
                        producerMap[method.ReturnType] = config;
                }
            }

            // 3. 构建 Configuration 间依赖图：A → B 表示 A 的 [Autowired] 字段依赖 B 的 [Bean] 产出
            var graph = new Dictionary<object, HashSet<object>>();
            foreach (var config in configs)
            {
                var deps = new HashSet<object>();
                var fields = config.GetType().GetFields(
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance)
                    .Where(f => f.GetCustomAttribute<AutowiredAttribute>() != null);

                foreach (var field in fields)
                {
                    if (producerMap.TryGetValue(field.FieldType, out var producer) && producer != config)
                        deps.Add(producer);
                }
                graph[config] = deps;
            }

            // 4. 拓扑排序 + 循环依赖检测
            var sortedConfigs = TopologicalSortConfigs(configs, graph);

            // 5. 按排序顺序执行：每个 [Bean] 方法调用前重新 Inject，确保同类内前置 Bean 已注入字段
            foreach (var config in sortedConfigs)
            {
                var methods = config.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<BeanAttribute>() != null);

                foreach (var method in methods)
                {
                    // 每次调用前重新注入，让同类内前置 [Bean] 产出的依赖生效
                    Inject(config);

                    var bean = method.Invoke(config, null);
                    if (bean == null) continue;

                    // 同时按返回类型和运行时类型注册，支持接口/基类注入
                    var returnType = method.ReturnType;
                    _beans[returnType] = bean;
                    var runtimeType = bean.GetType();
                    if (runtimeType != returnType)
                        _beans[runtimeType] = bean;
                    if (!_instances.Contains(bean))
                        _instances.Add(bean);
                }
            }
        }

        /// <summary>
        /// DFS 拓扑排序 Configuration 类，检测循环依赖。
        /// 依赖项排在前面（先产出被依赖的 Bean）。
        /// </summary>
        private List<object> TopologicalSortConfigs(List<object> configs, Dictionary<object, HashSet<object>> graph)
        {
            var sorted = new List<object>();
            var state = new Dictionary<object, int>(); // 0=未访问, 1=访问中, 2=已完成

            foreach (var config in configs)
                state[config] = 0;

            foreach (var config in configs)
            {
                if (state[config] == 0)
                {
                    var path = new List<object>();
                    DfsVisit(config, graph, state, sorted, path);
                }
            }

            return sorted;
        }

        private void DfsVisit(object node, Dictionary<object, HashSet<object>> graph,
            Dictionary<object, int> state, List<object> sorted, List<object> path)
        {
            if (state[node] == 2) return;

            if (state[node] == 1)
            {
                // 发现回边 → 循环依赖
                var cycleStart = path.IndexOf(node);
                var cycleNames = path.Skip(cycleStart)
                    .Select(n => n.GetType().Name)
                    .ToList();
                cycleNames.Add(node.GetType().Name);
                throw new InvalidOperationException(
                    $"[ApplicationContext] 检测到 [Configuration] 循环依赖:\n" +
                    $"  {string.Join(" → ", cycleNames)}\n" +
                    "建议：使用字段注入打破循环，或重构为无环依赖链。");
            }

            state[node] = 1;
            path.Add(node);

            if (graph.TryGetValue(node, out var deps))
            {
                foreach (var dep in deps)
                    DfsVisit(dep, graph, state, sorted, path);
            }

            path.RemoveAt(path.Count - 1);
            state[node] = 2;
            sorted.Add(node); // 依赖项先加入
        }

        /// <summary>
        /// 注入所有已注册实例的 [Autowired] 字段
        /// </summary>
        public void InjectAll()
        {
            foreach (var instance in _instances)
                Inject(instance);
        }

        /// <summary>
        /// 调用所有 [PostConstruct] 方法（等价 Spring @PostConstruct）
        /// </summary>
        public void PostConstruct()
        {
            foreach (var instance in _instances)
            {
                var methods = instance.GetType().GetMethods(
                    BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.GetCustomAttribute<PostConstructAttribute>() != null);

                foreach (var method in methods)
                    method.Invoke(instance, null);
            }
        }

        /// <summary>
        /// 校验所有 [Autowired] 字段都已成功注入，未注入则抛出异常。
        /// 应在 InjectAll 之后调用。
        /// </summary>
        public void Validate()
        {
            var errors = new List<string>();

            foreach (var instance in _instances)
            {
                foreach (var field in GetAutowiredFields(instance.GetType()))
                {
                    if (field.GetValue(instance) == null)
                    {
                        errors.Add($"  • {instance.GetType().Name}.{field.Name} ({field.FieldType.Name})");
                    }
                }
            }

            if (errors.Count > 0)
            {
                throw new InvalidOperationException(
                    "[ApplicationContext] 以下 [Autowired] 依赖未找到对应的 Bean:\n" +
                    string.Join("\n", errors) +
                    "\n请检查对应类型是否已注册（[Component] 或 [Bean] 产出）。");
            }
        }
    }
}
