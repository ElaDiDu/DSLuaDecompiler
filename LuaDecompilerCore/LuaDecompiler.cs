﻿using System;
using System.Collections.Generic;
using System.Linq;
using LuaDecompilerCore.IR;
using LuaDecompilerCore.Utilities;

namespace LuaDecompilerCore
{
    public class LuaDecompiler
    {
        private DecompilationOptions DecompilationOptions { get; set; }
        private readonly GlobalSymbolTable _globalSymbolTable = new();

        public LuaDecompiler(DecompilationOptions options)
        {
            DecompilationOptions = options;
        }

        public string DecompileLuaFunction(
            ILanguageDecompiler languageDecompiler,
            Function main,
            LuaFile.Function luaMain)
        {
            var functions = new List<Function>();
            
            void Visit(LuaFile.Function luaFunction, Function irFunction)
            {
                // Language specific initialization
                languageDecompiler.InitializeFunction(luaFunction, irFunction, _globalSymbolTable);
                
                // Enable debug comments if set
                irFunction.InsertDebugComments = DecompilationOptions.OutputDebugComments;

                // Initialize function parameters
                var parameters = new List<Identifier>();
                for (uint i = 0; i < luaFunction.NumParams; i++)
                {
                    parameters.Add(irFunction.GetRegister(i));
                }
                irFunction.SetParameters(parameters);

                // Now generate IR from the bytecode using language specific decompiler
                languageDecompiler.GenerateIr(luaFunction, irFunction, _globalSymbolTable);
                
                // Add function to run decompilation passes on
                functions.Add(irFunction);
                
                // Now visit all the child closures unless they are excluded
                for (var i = 0; i < luaFunction.ChildFunctions.Length; i++)
                {
                    if (DecompilationOptions.IncludedFunctionIds != null && 
                        !DecompilationOptions.IncludedFunctionIds.Contains(luaFunction.ChildFunctions[i].FunctionID))
                        continue;
                    if (DecompilationOptions.ExcludedFunctionIds.Contains(luaFunction.ChildFunctions[i].FunctionID))
                        continue;
                    Visit(luaFunction.ChildFunctions[i], irFunction.LookupClosure((uint)i));
                }
            }
            
            // Visit all the functions and generate the initial IR from the bytecode
            Visit(luaMain, main);

            // Create pass manager, add language specific passes, and run
            var passManager = new PassManager(DecompilationOptions);
            languageDecompiler.AddDecompilePasses(passManager);
            return passManager.RunOnFunctions(new DecompilationContext(_globalSymbolTable), functions);
        }
    }
}