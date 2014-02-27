﻿using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using NuGet;
using Microsoft.Net.Runtime.Loader;
using Microsoft.Net.Runtime.FileSystem;

#if NET45 // TODO: Temporary due to CoreCLR and Desktop Roslyn being out of sync
using EmitResult = Microsoft.CodeAnalysis.Emit.CommonEmitResult;
#endif

namespace Microsoft.Net.Runtime.Roslyn
{
    public class RoslynAssemblyLoader : IAssemblyLoader
    {
        private readonly Dictionary<string, CompilationContext> _compilationCache = new Dictionary<string, CompilationContext>();

        private readonly IRoslynCompiler _compiler;
        private readonly IAssemblyLoaderEngine _loaderEngine;
        private readonly IProjectResolver _projectResolver;
        private readonly IResourceProvider _resourceProvider;

        public RoslynAssemblyLoader(IAssemblyLoaderEngine loaderEngine,
                                    IFileWatcher watcher,
                                    IProjectResolver projectResolver,
                                    IDependencyExporter dependencyExporter)
        {
            _loaderEngine = loaderEngine;
            _projectResolver = projectResolver;

            var globalAssemblyCache = new DefaultGlobalAssemblyCache();
            var frameworkResolver = new FrameworkReferenceResolver(globalAssemblyCache);

            var resxProvider = new ResxResourceProvider();
            var embeddedResourceProvider = new EmbeddedResourceProvider();

            _resourceProvider = new CompositeResourceProvider(new IResourceProvider[] { resxProvider, embeddedResourceProvider });
            _compiler = new RoslynCompiler(projectResolver, 
                                           watcher, 
                                           frameworkResolver, 
                                           dependencyExporter);
            
        }

        public AssemblyLoadResult Load(LoadContext loadContext)
        {
            var compilationContext = GetCompilationContext(loadContext.AssemblyName, loadContext.TargetFramework);

            if (compilationContext == null)
            {
                return null;
            }

            var project = compilationContext.Project;
            var path = project.ProjectDirectory;
            var name = project.Name;

            var resources = _resourceProvider.GetResources(project);

            foreach (var reference in compilationContext.AssemblyNeutralReferences)
            {
                resources.Add(new ResourceDescription(reference.Name + ".dll",
                                                      () => reference.OutputStream,
                                                      isPublic: true));
            }

            return CompileInMemory(name, compilationContext, resources);
        }

        private CompilationContext GetCompilationContext(string name, FrameworkName targetFramework)
        {
            CompilationContext compilationContext;
            if (_compilationCache.TryGetValue(name, out compilationContext))
            {
                return compilationContext;
            }

            var context = _compiler.CompileProject(name, targetFramework);

            if (context == null)
            {
                return null;
            }

            CacheCompilation(context);

            return context;
        }

        private void CacheCompilation(CompilationContext context)
        {
            _compilationCache[context.Project.Name] = context;

            foreach (var ctx in context.ProjectReferences)
            {
                CacheCompilation(ctx);
            }
        }

        private AssemblyLoadResult CompileInMemory(string name, CompilationContext compilationContext, IEnumerable<ResourceDescription> resources)
        {
            using (var pdbStream = new MemoryStream())
            using (var assemblyStream = new MemoryStream())
            {
#if NET45
                EmitResult result = compilationContext.Compilation.Emit(assemblyStream, pdbStream: pdbStream, manifestResources: resources);
#else
                EmitResult result = compilationContext.Compilation.Emit(assemblyStream);
#endif

                if (!result.Success)
                {
                    return ReportCompilationError(compilationContext.Diagnostics.Concat(result.Diagnostics));
                }

                if (compilationContext.Diagnostics.Count > 0)
                {
                    return ReportCompilationError(compilationContext.Diagnostics);
                }

                var assemblyBytes = assemblyStream.ToArray();
                byte[] pdbBytes = null;
#if NET45
                pdbBytes = pdbStream.ToArray();
#endif

                var assembly = _loaderEngine.LoadBytes(assemblyBytes, pdbBytes);

                return new AssemblyLoadResult(assembly);
            }
        }

        private static AssemblyLoadResult ReportCompilationError(IEnumerable<Diagnostic> results)
        {
            return new AssemblyLoadResult(GetErrors(results));
        }

        private static List<string> GetErrors(IEnumerable<Diagnostic> diagnostis)
        {
#if NET45 // TODO: Temporary due to CoreCLR and Desktop Roslyn being out of sync
            var formatter = DiagnosticFormatter.Instance;
#else
            var formatter = new DiagnosticFormatter();
#endif
            var errors = new List<string>(diagnostis.Select(d => formatter.Format(d)));

            return errors;
        }
    }
}
