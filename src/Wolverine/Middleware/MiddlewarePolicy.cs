using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using JasperFx.CodeGeneration;
using JasperFx.CodeGeneration.Frames;
using JasperFx.CodeGeneration.Model;
using JasperFx.Core;
using JasperFx.Core.Reflection;
using Lamar;
using Wolverine.Configuration;
using Wolverine.Runtime.Handlers;

namespace Wolverine.Middleware;

/// <summary>
/// Starts a new coding block with the supplied code
/// </summary>
public class StartBlock : SyncFrame
{
    private readonly string _code;

    public static StartBlock ForTry() => new StartBlock("try");

    /// <summary>
    /// Start a new code block with the supplied code 
    /// </summary>
    /// <param name="code"></param>
    public StartBlock(string code)
    {
        _code = code;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.Write("BLOCK:" + _code);
        Next?.GenerateCode(method, writer);
    }
}

/// <summary>
/// Writes the ending code block
/// </summary>
public class EndBlock : SyncFrame
{
    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        writer.FinishBlock();
        Next?.GenerateCode(method, writer);
    }
}

// TODO -- move this to JasperFx.CodeGeneration if it works
public class TryFinallyWrapperFrame : Frame
{
    private readonly Frame _inner;
    private readonly Frame[] _finallys;

    public TryFinallyWrapperFrame(Frame inner, Frame[] finallys) : base(inner.IsAsync)
    {
        _inner = inner;
        _finallys = finallys;
    }

    public override void GenerateCode(GeneratedMethod method, ISourceWriter writer)
    {
        _inner.GenerateCode(method, writer);
        writer.Write("BLOCK:try");
        
        Next?.GenerateCode(method, writer);
        
        writer.FinishBlock();
        writer.Write("BLOCK:finally");
        
        if (_finallys.Length > 1)
        {
            for (var i = 1; i < _finallys.Length; i++)
            {
                _finallys[i - 1].Next = _finallys[i];
            }
        }
        
        _finallys[0].GenerateCode(method, writer);
        
        writer.FinishBlock();
    }

    public override IEnumerable<Variable> FindVariables(IMethodVariables chain)
    {
        foreach (var variable in _inner.FindVariables(chain))
        {
            yield return variable;
        }

        // NOT letting the finallys get involved with ordering frames
        // because that way lies madness
        foreach (var @finally in _finallys)
        {
            // Forcing it to evaluate and attach all variables
            @finally.FindVariables(chain).ToArray();
        }
    }
}

// TODO -- move to JasperFx.CodeGeneration
public static class FrameExtensions
{
    public static bool CreatesNewOf<T>(this MethodCall frame)
    {
        return frame.ReturnVariable?.VariableType == typeof(T) || frame.Creates.Any(x => x.VariableType == typeof(T));
    }
}

internal class MiddlewarePolicy : IHandlerPolicy
{
    public static readonly string[] BeforeMethodNames = { "Before", "BeforeAsync", "Load", "LoadAsync" };
    public static readonly string[] AfterMethodNames = { "After", "AfterAsync", "PostProcess", "PostProcessAsync" };
    public static readonly string[] FinallyMethodNames = {"Finally", "FinallyAsync"};

    private readonly List<Application> _applications = new();

    public void Apply(HandlerGraph graph, GenerationRules rules, IContainer container)
    {
        foreach (var chain in graph.Chains)
        {
            var befores = _applications.SelectMany(x => x.BuildBeforeCalls(chain)).ToArray();

            for (var i = 0; i < befores.Length; i++)
            {
                chain.Middleware.Insert(i, befores[i]);
            }

            var afters = _applications.ToArray().Reverse().SelectMany(x => x.BuildAfterCalls(chain));

            chain.Postprocessors.AddRange(afters);
        }
    }


    public Application AddType(Type middlewareType, Func<HandlerChain, bool>? filter = null)
    {
        filter ??= _ => true;
        var application = new Application(middlewareType, filter);
        _applications.Add(application);
        return application;
    }

    public class Application
    {
        private readonly MethodInfo[] _afters;
        private readonly MethodInfo[] _befores;
        private readonly MethodInfo[] _finals;
        private readonly ConstructorInfo? _constructor;

        public Application(Type middlewareType, Func<HandlerChain, bool> filter)
        {
            if (!middlewareType.IsPublic)
            {
                throw new InvalidWolverineMiddlewareException(middlewareType);
            }

            if (!middlewareType.IsStatic())
            {
                var constructors = middlewareType.GetConstructors();
                if (constructors.Length != 1)
                {
                    throw new InvalidWolverineMiddlewareException(middlewareType);
                }

                _constructor = constructors.Single();
            }

            MiddlewareType = middlewareType;
            Filter = filter;

            var methods = middlewareType.GetMethods().ToArray();

            _befores = methods.Where(x => BeforeMethodNames.Contains(x.Name)).ToArray();
            _afters = methods.Where(x => AfterMethodNames.Contains(x.Name)).ToArray();
            _finals = methods.Where(x => FinallyMethodNames.Contains(x.Name)).ToArray();

            if (!_befores.Any() && !_afters.Any())
            {
                throw new InvalidWolverineMiddlewareException(middlewareType);
            }
        }

        public Type MiddlewareType { get; }
        public Func<HandlerChain, bool> Filter { get; }


        public bool MatchByMessageType { get; set; }

        public IEnumerable<Frame> BuildBeforeCalls(HandlerChain chain)
        {
            var frames = buildBefores(chain).ToArray();
            if (frames.Any() && !MiddlewareType.IsStatic())
            {
                var constructorFrame = new ConstructorFrame(MiddlewareType, _constructor);
                if (MiddlewareType.CanBeCastTo<IDisposable>() || MiddlewareType.CanBeCastTo<IAsyncDisposable>())
                {
                    constructorFrame.Mode = ConstructorCallMode.UsingNestedVariable;
                }

                yield return constructorFrame;
            }

            foreach (var frame in frames) yield return frame;
        }

        private IEnumerable<Frame> wrapBeforeFrame(MethodCall call)
        {
            if (_finals.Any())
            {
                if (call.CreatesNewOf<HandlerContinuation>())
                {
                    call.Next = new HandlerContinuationFrame(call);
                }

                var finals = _finals.Select(x => new MethodCall(MiddlewareType, x)).ToArray();

                yield return new TryFinallyWrapperFrame(call, finals);
            }
            else
            {
                yield return call;
                if (call.CreatesNewOf<HandlerContinuation>())
                {
                    yield return new HandlerContinuationFrame(call);
                }
            }
        }

        private MethodCall? buildCallForBefore(HandlerChain chain, MethodInfo before)
        {
            if (MatchByMessageType)
            {
                var messageType = before.MessageType();
                if (messageType != null && chain.MessageType.CanBeCastTo(messageType))
                {
                    return new MethodCallAgainstMessage(MiddlewareType, before, messageType);
                }
            }
            else
            {
                return new MethodCall(MiddlewareType, before);
            }

            return null;
        }

        private IEnumerable<Frame> buildBefores(HandlerChain chain)
        {
            if (!Filter(chain))
            {
                yield break;
            }

            foreach (var before in _befores)
            {
                var call = buildCallForBefore(chain, before);
                if (call != null)
                {
                    foreach (var frame in wrapBeforeFrame(call))
                    {
                        yield return frame;
                    }
                }
            }
        }

        public IEnumerable<Frame> BuildAfterCalls(HandlerChain chain)
        {
            var afters = buildAfters(chain).ToArray();

            if (afters.Any() && !MiddlewareType.IsStatic())
            {
                if (chain.Middleware.OfType<ConstructorFrame>().All(x => x.Variable.VariableType != MiddlewareType))
                {
                    yield return new ConstructorFrame(MiddlewareType, _constructor);
                }
            }

            foreach (var after in afters) yield return after;
        }

        private IEnumerable<Frame> buildAfters(HandlerChain chain)
        {
            if (!Filter(chain))
            {
                yield break;
            }

            foreach (var after in _afters)
            {
                if (MatchByMessageType)
                {
                    var messageType = after.MessageType();
                    if (messageType != null && chain.MessageType.CanBeCastTo(messageType))
                    {
                        yield return new MethodCallAgainstMessage(MiddlewareType, after, messageType);
                    }
                }
                else
                {
                    yield return new MethodCall(MiddlewareType, after);
                }
            }
        }
    }
}