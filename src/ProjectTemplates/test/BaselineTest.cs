// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using ProjectTemplates.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Templates.Test
{
    public class BaselineTest
    {
        public BaselineTest(ProjectFactoryFixture projectFactory, ITestOutputHelper output)
        {
            ProjectFactory = projectFactory;
            Output = output;
        }

        public Project Project { get; set; }

        public static TheoryData<string, string[]> TemplateBaselines
        {
            get
            {
                using (var stream = typeof(BaselineTest).Assembly.GetManifestResourceStream("ProjectTemplates.Tests.template-baselines.json"))
                {
                    using (var jsonReader = new JsonTextReader(new StreamReader(stream)))
                    {
                        var baseline = JObject.Load(jsonReader);
                        var data = new TheoryData<string, string[]>();
                        foreach (var template in baseline)
                        {
                            foreach (var authOption in (JObject)template.Value)
                            {
                                data.Add(
                                    (string)authOption.Value["Arguments"],
                                    ((JArray)authOption.Value["Files"]).Select(s => (string)s).ToArray());
                            }
                        }

                        return data;
                    }
                }
            }
        }

        public ProjectFactoryFixture ProjectFactory { get; }
        public ITestOutputHelper Output { get; }

        [Theory]
        [MemberData(nameof(TemplateBaselines))]
        public void Template_Produces_The_Right_Set_Of_Files(string arguments, string[] expectedFiles)
        {
            Project = ProjectFactory.CreateProject(SanitizeArgs(arguments), Output);
            Project.RunDotNet(arguments);
            foreach (var file in expectedFiles)
            {
                Project.AssertFileExists(file, shouldExist: true);
            }

            var filesInFolder = Directory.EnumerateFiles(Project.TemplateOutputDir, "*", SearchOption.AllDirectories);
            foreach (var file in filesInFolder)
            {
                var relativePath = file.Replace(Project.TemplateOutputDir, "").Replace("\\", "/").Trim('/');
                if (relativePath.EndsWith(".csproj", StringComparison.Ordinal) ||
                    relativePath.EndsWith(".fsproj", StringComparison.Ordinal) ||
                    relativePath.EndsWith(".props", StringComparison.Ordinal) ||
                    relativePath.EndsWith(".targets", StringComparison.Ordinal) ||
                    relativePath.StartsWith("bin/", StringComparison.Ordinal) ||
                    relativePath.StartsWith("obj/", StringComparison.Ordinal))
                {
                    continue;
                }
                Assert.Contains(relativePath, expectedFiles);
            }
        }

        private string SanitizeArgs(string arguments)
        {
            
            var text = Regex.Match(arguments, "new (?<template>[a-zA-Z]+)").Groups.TryGetValue("template", out var template) ?
                template.Value : "";

            
            text += Regex.Match(arguments, "-au (?<auth>[a-zA-Z]+)").Groups.TryGetValue("auth", out var auth) ?
                auth.Value : "";

            text += arguments.Contains("--uld") ? "uld" : "";

            text += Regex.Match(arguments, "--language (?<language>\\w+)").Groups.TryGetValue("language", out var language) ?
                language.Value.Replace("#", "Sharp") : "";

            return text;
        }
    }
}
