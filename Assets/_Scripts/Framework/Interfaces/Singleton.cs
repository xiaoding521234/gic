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


    public class Singleton<T>
    {
        private static readonly T instance = Activator.CreateInstance<T>();

        public static T Instance
        {
            get {
                return instance;
            }
        }
        public virtual void Init()
        {

        }

    }

}

