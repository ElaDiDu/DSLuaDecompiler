using LuaDecompilerCore.CFG;
using LuaDecompilerCore.IR;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace LuaDecompilerCore.Passes
{
    /// <summary>
    /// Fixes any repeat variable names using dominance by adding a suffix
    /// </summary>
    public class HandleRepeatVariableNamesPass : IPass
    {
        public bool RunOnFunction(DecompilationContext decompilationContext, FunctionContext functionContext, Function f)
        {
            // What's the point of a pass if you only use it on main function?
            if (f.FunctionId == 0)
                HandleRepeatNames(f);
            
            return false;
        }

        private static void HandleRepeatNames(Function mainFunc) 
        {
            HandleRepeatNames(mainFunc, mainFunc.BeginBlock, new Dictionary<string, int>(), true);
        }

        //TODO Ugly code duplicates
        private static void HandleRepeatNames(Function func, BasicBlock block, Dictionary<string, int> nameCount, bool newFunc)
        {
            if (newFunc)
            {
                for (int i = 0; i < func.ParameterCount; i++)
                {
                    var id = Identifier.GetRegister((uint)i);
                    func.ParameterNames.TryGetValue(id, out var name);
                    if (name == null) continue;

                    if (!nameCount.ContainsKey(name))
                    {
                        // Make sure no locals are named after existing globals
                        int countSuffix = FindNearestNonGlobalSuffix(block, name, 1);
                        nameCount[name] = countSuffix;

                        // First name has no suffix
                        if (countSuffix > 1) 
                        {
                            func.ParameterNames[id] = $"{name}_{countSuffix}";
                        }
                    }
                    else
                    {
                        // Make sure no locals are named after existing globals
                        int countSuffix = FindNearestNonGlobalSuffix(block, name, nameCount[name] + 1);
                        nameCount[name] = countSuffix;
                        func.ParameterNames[id] = $"{name}_{countSuffix}";
                    }
                }
            }

            foreach (var id in block.LocalsDefined.OrderBy(i => i.RegNum))
            {
                block.IdentifierNames.TryGetValue(id, out var name);
                if (name == null) continue;

                if (!nameCount.ContainsKey(name))
                {
                    // Make sure no locals are named after existing globals
                    int countSuffix = FindNearestNonGlobalSuffix(block, name, 1);
                    nameCount[name] = countSuffix;

                    // First name has no suffix
                    if (countSuffix > 1)
                    {
                        block.IdentifierNames[id] = $"{name}_{countSuffix}";
                    }
                }
                else
                {
                    // Make sure no locals are named after existing globals
                    int countSuffix = FindNearestNonGlobalSuffix(block, name, nameCount[name] + 1);
                    nameCount[name] = countSuffix;
                    block.IdentifierNames[id] = $"{name}_{countSuffix}";
                }
            }

            foreach (var dominated in block.DominatesImmediate)
            {
                HandleRepeatNames(func, func.BlockList[(int)dominated], new Dictionary<string, int>(nameCount), false);
            }

            foreach (var child in func.Closures)
            {
                if (child.ParentBlockDefinition == block)
                    HandleRepeatNames(child, child.BeginBlock, new Dictionary<string, int>(nameCount), true);
            }
        }

        /// <summary>
        /// Return the suffix to a variable name to avoid global collisions in scope.
        /// Testing the name from the given count up until you reach a name not in the globals.
        /// </summary>
        /// <param name="block"></param>
        /// <param name="name"></param>
        /// <param name="count">count of identifiers with this name</param>
        /// <returns></returns>
        private static int FindNearestNonGlobalSuffix(BasicBlock block, string name, int count)
        {
            var correctedName = name;
            while (block.GlobalsReferenced.Contains(correctedName))
            {
                count++;
                correctedName = $"{name}_{count}";
            }

            return count;
        }
    }
}
