using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System;
using GIC.Data;
using GIC.Data.Event;
using GIC.UI;
using GIC.Battle;
using GIC.Tool;
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

