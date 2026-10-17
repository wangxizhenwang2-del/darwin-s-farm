using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Darwin.Rendering.Validation
{
    public static class DarwinMenuValidation
    {
        public static void Run()
        {
            Directory.CreateDirectory("Validation/MenuCleanup");var notes=new List<string>();
            try
            {
                if(!Application.dataPath.Replace('\\','/').Contains("/Temp/")||!File.Exists("MenuCleanup.marker"))throw new Exception("Use the isolated fixture.");
                var entries=TypeCache.GetMethodsWithAttribute<MenuItem>().SelectMany(m=>m.GetCustomAttributes(typeof(MenuItem),false).Cast<MenuItem>().Select(a=>(method:m,attribute:a))).ToArray();
                var menus=entries.Where(e=>!e.attribute.validate).Select(e=>e.attribute.menuItem).ToArray();
                var root=menus.Where(p=>p.StartsWith("Darwin",StringComparison.OrdinalIgnoreCase)).ToArray();
                if(root.Length!=0)throw new Exception("Unexpected custom top-level Darwin menu: "+string.Join(", ",root));
                var project=entries.Where(e=>e.attribute.menuItem.StartsWith("Tools/Darwin Farm/",StringComparison.Ordinal)&&!e.attribute.validate).ToArray();
                if(project.Length!=11)throw new Exception("Expected 11 grouped project tools, found "+project.Length);
                if(project.GroupBy(e=>e.attribute.menuItem).Any(g=>g.Count()!=1))throw new Exception("Duplicate project menu registration");
                if(menus.Any(p=>p.StartsWith("Tools/Darwin's Farm/",StringComparison.Ordinal)||p.StartsWith("Tools/Population/",StringComparison.Ordinal)))throw new Exception("Old scattered project menu path remains");
                foreach(var item in project.OrderBy(e=>e.attribute.menuItem))
                {
                    if(!item.method.IsStatic||item.method.GetParameters().Length!=0)throw new Exception("Invalid command signature: "+item.method);
                    notes.Add(item.attribute.menuItem+" => "+item.method.DeclaringType.FullName+"."+item.method.Name);
                }
                notes.Add("PASS: all 11 original commands retained under Tools/Darwin Farm; no standalone Darwin root or duplicate command path.");
                notes.Add("PASS: existing Moebius commands retained: "+menus.Count(p=>p.StartsWith("Tools/Moebius/",StringComparison.Ordinal)));
                // Discover the installed editor's native menu inspection API without invoking any tools.
                var menuType=typeof(EditorApplication).Assembly.GetType("UnityEditor.Menu");
                foreach(var method in menuType.GetMethods(BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic).Where(m=>m.Name.Contains("MenuItems")||m.Name.Contains("TopLevel")))
                    notes.Add("Native menu API: "+method);
                var nativeApi=menuType.GetMethod("GetMenuItems",BindingFlags.Static|BindingFlags.Public|BindingFlags.NonPublic,null,new[]{typeof(string),typeof(bool),typeof(bool)},null);
                var native=(Array)nativeApi.Invoke(null,new object[]{"Tools/Darwin Farm",false,false});
                notes.Add("Native project menu entries: "+native.Length);
                foreach(var item in native)
                {
                    foreach(var field in item.GetType().GetFields(BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic).Where(f=>f.FieldType==typeof(string)))
                        notes.Add("Native "+field.Name+": "+field.GetValue(item));
                }
                notes.Add("Errors: 0");File.WriteAllLines("Validation/MenuCleanup/Report.txt",notes);EditorApplication.Exit(0);
            }
            catch(Exception e){notes.Add(e.ToString());File.WriteAllLines("Validation/MenuCleanup/Report.txt",notes);EditorApplication.Exit(1);}
        }
    }
}
