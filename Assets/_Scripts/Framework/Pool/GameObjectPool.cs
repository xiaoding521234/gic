using System.Collections.Generic;
using UnityEngine;

namespace GIC.Framework
{
    /// <summary>
    /// 通用 GameObject 对象池 — Stack-based，O(1) 出入栈。
    /// 调用方管理池的生命周期，池本身不继承 MonoBehaviour。
    /// </summary>
    public class GameObjectPool
    {
        private readonly GameObject _prefab;
        private readonly Transform _parent;
        private readonly Stack<GameObject> _pool = new();

        /// <summary>
        /// 当前池中空闲对象数量
        /// </summary>
        public int Count => _pool.Count;

        /// <summary>
        /// 创建对象池
        /// </summary>
        /// <param name="prefab">实例化用的预制体</param>
        /// <param name="parent">回收对象的父容器</param>
        /// <param name="prewarm">预预热数量（构造时预创建 N 个对象）</param>
        public GameObjectPool(GameObject prefab, Transform parent, int prewarm = 0)
        {
            _prefab = prefab;
            _parent = parent;

            for (int i = 0; i < prewarm; i++)
            {
                var obj = CreateObject();
                obj.SetActive(false);
                _pool.Push(obj);
            }
        }

        /// <summary>
        /// 从池中获取对象（无可用时实例化新的）
        /// </summary>
        public GameObject Get(Vector3 position = default, Quaternion rotation = default)
        {
            GameObject obj;
            if (_pool.Count > 0)
            {
                obj = _pool.Pop();
                obj.transform.SetPositionAndRotation(position, rotation);
            }
            else
            {
                obj = CreateObject();
                obj.transform.SetPositionAndRotation(position, rotation);
            }

            obj.SetActive(true);
            return obj;
        }

        /// <summary>
        /// 从池中获取对象并返回指定组件
        /// </summary>
        public T Get<T>(Vector3 position = default, Quaternion rotation = default) where T : Component
        {
            return Get(position, rotation).GetComponent<T>();
        }

        /// <summary>
        /// 将对象归还池中（禁用并归位到父容器）
        /// </summary>
        public void Release(GameObject obj)
        {
            if (obj == null) return;
            obj.SetActive(false);
            if (_parent != null)
                obj.transform.SetParent(_parent, false);
            _pool.Push(obj);
        }

        /// <summary>
        /// 销毁所有缓存的对象
        /// </summary>
        public void Clear()
        {
            foreach (var obj in _pool)
            {
                if (obj != null) Object.Destroy(obj);
            }
            _pool.Clear();
        }

        private GameObject CreateObject()
        {
            var obj = _parent != null
                ? Object.Instantiate(_prefab, _parent, false)
                : Object.Instantiate(_prefab);
            return obj;
        }
    }
}
