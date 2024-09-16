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
                        if (block.GlobalsReferenced.Contains(name))
                        {
                            nameCount[name] = 2;
                            block.IdentifierNames[id] = $"{name}_{nameCount[name]}";
                        }
                        else
                            nameCount[name] = 1;
                    }
                    else
                    {
                        nameCount[name]++;
                        func.ParameterNames[id] = $"{name}_{nameCount[name]}";
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
                    if (block.GlobalsReferenced.Contains(name))
                    {
                        nameCount[name] = 2;
                        block.IdentifierNames[id] = $"{name}_{nameCount[name]}";
                    }
                    else
                        nameCount[name] = 1;
                }
                else
                {
                    nameCount[name]++;
                    block.IdentifierNames[id] = $"{name}_{nameCount[name]}";
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
    }
}
