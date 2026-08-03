using System;
using System.Collections;
using UnityEngine;

namespace VRLogConsole.Example.Scripts
{
   public class LogTest : MonoBehaviour
   {
      private IEnumerator Start()
      {
         yield return new WaitForSeconds(1);
         
         Debug.Log("This is an info log"); 
         
         yield return new WaitForSeconds(1);
         
         Debug.LogWarning("This is a warning log");
         
         yield return new WaitForSeconds(1);
         
         Debug.LogException(new Exception("This is an exception log"));
      }
   }
}
