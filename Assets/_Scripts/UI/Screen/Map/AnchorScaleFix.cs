using System.Collections;
using UnityEngine;
using GIC.Framework;
using GIC.Data;
using GIC.Data.Event;
using GIC.Battle;
using GIC.Tool;
namespace GIC.UI
{



    public class AnchorScaleFix : MonoBehaviour
    {
        private Vector3 initialScale;
        private Transform parentContent;
        
        private void Start()
        {
            initialScale = transform.localScale;
            parentContent = transform.parent.parent;

        }
        
        private void Update()
        {
            // 反向缩放：父物体放大，锚点就缩小
            if (parentContent != null)
            {
                float parentScale = parentContent.localScale.x;
                if (parentScale != 0)
                {
                    transform.localScale = initialScale / parentScale;
                }
            }
        }
    }
}

