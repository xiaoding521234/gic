using System;
namespace GIC.Framework
{


    public class Singleton<T> where T : class
    {
        private static T _instance;

        public static T Instance
        {
            get { return _instance ??= Activator.CreateInstance<T>(); }
        }

        public virtual void Init()
        {

        }

    }

}

