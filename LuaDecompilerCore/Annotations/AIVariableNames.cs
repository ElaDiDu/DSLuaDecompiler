﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace LuaDecompilerCore.Annotations
{
    public class AIVariableNames
    {
        /// <summary>
        /// Contains info about args names
        /// </summary>
        public Dictionary<string, string[]> funcs { get; set; }

        /// <summary>
        /// Contains info about args names and return names
        /// </summary>
        public Dictionary<string, CallNamingData> calls { get; set; }

        /// <summary>
        /// Contains info about goal args names
        /// </summary>
        public Dictionary<string, string[]> goals { get; set; }

        public Dictionary<string, string> globalsTranslatedForAppending { get; set; }


        public AIVariableNames()
        {
            funcs = new();
            calls = new();
            goals = new();
            globalsTranslatedForAppending = new();
        }

        public string[]? getCallArgs(string funcName) 
        {
            var callData = calls.TryGetValue(funcName, out var o) ? o : null;
            if (callData == null) return null;

            return callData.args;
        }

        public string[]? getCallReturns(string funcName)
        {
            var callData = calls.TryGetValue(funcName, out var o) ? o : null;
            if (callData == null) return null;

            return callData.@return;
        }

        public int[]? getArgsToAppendToReturn(string funcName) 
        {
            var callData = calls.TryGetValue(funcName, out var o) ? o : null;
            if (callData == null) return null;

            return callData.argsToAppendToReturn;
        }

        public string[]? getGoalArgs(string goalName)
        {
            return goals.TryGetValue(goalName, out var o) ? o : null;
        }

        // Act992 or Act2 or Act07
        private static Regex actPattern = new Regex(@"\w*Act[0-9][0-9]?[0-9]?\b");

        public string[]? getFuncArgs(string funcName)
        {
            // Take care of non-table goals
            if (funcName.EndsWith("_Activate"))
                return funcs.TryGetValue("GlobalActivate", out var args) ? args : null;
            if (funcName.EndsWith("_Update"))
                return funcs.TryGetValue("GlobalUpdate", out var args) ? args : null;
            if (funcName.EndsWith("_Terminate"))
                return funcs.TryGetValue("GlobalTerminate", out var args) ? args : null;
            if (funcName.EndsWith("_Interupt")) // The typo is intentional
                return funcs.TryGetValue("GlobalInterrupt", out var args) ? args : null;

            // Acts
            if (actPattern.IsMatch(funcName) || funcName.Contains("ActAfter"))
                return funcs.TryGetValue("Acts", out var args) ? args : null;

            return funcs.TryGetValue(funcName, out var argsO) ? argsO : null;
        }

        public string? translateGlobalForAppending(string global) 
        {
            return globalsTranslatedForAppending.TryGetValue(global, out var arg) ? arg : global;
        }

        public static AIVariableNames FromJson(string path) 
        {
            FileStream stream = File.OpenRead(path);
            AIVariableNames? result = JsonSerializer.Deserialize<AIVariableNames>(stream);
            if (result == null) 
            {
                return new AIVariableNames();
            }

            return result;
        }
    }

    public class CallNamingData 
    {
        public string[]? @return { get; set; }

        public string[]? args { get; set; }

        public int[]? argsToAppendToReturn { get; set; }

        public CallNamingData() { }
    }
}
