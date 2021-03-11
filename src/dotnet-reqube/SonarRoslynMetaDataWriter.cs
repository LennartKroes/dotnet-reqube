using ReQube.Logging;
using ReQube.Utils;
using Serilog;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ReQube
{
    public class SonarRoslynMetaDataWriter : ISonarMetaDataWriter
    {
        private readonly DirectoryInfo _sonarDirInfo;

        private ILogger Logger { get; } = LoggerFactory.GetLogger();        

        public SonarRoslynMetaDataWriter(string sonarDir)
        {            
            _sonarDirInfo = new DirectoryInfo(sonarDir);            
        }

        public void AddReSharperAnalysisPaths(IDictionary<string, string> reportPathsByProject)
        {
            var projectInfoFiles = _sonarDirInfo.GetFiles(
                "ProjectInfo.xml",
                new EnumerationOptions
                {
                    RecurseSubdirectories = true
                });

            foreach (var projectInfoFile in projectInfoFiles)
            {
                AddReSharperAnalysisPaths(projectInfoFile, reportPathsByProject);
            }
        }

        private void AddReSharperAnalysisPaths(
            FileInfo projectInfoFile, IDictionary<string, string> reportPathsByProject)
        {
            var projectInfo = XElement.Load(projectInfoFile.FullName);
            var ns = projectInfo.GetDefaultNamespace();

            var projectPath = projectInfo.RequiredElement(ns + "FullPath").Value;
            reportPathsByProject.TryGetValue(
                Path.GetFileNameWithoutExtension(projectPath), out var reSharperRoslynFile);

            if (reSharperRoslynFile == null || !File.Exists(reSharperRoslynFile))
            {
                Logger.Information(
                    $"{reSharperRoslynFile} is not found, no changes to {projectInfoFile.FullName} are needed.");
                return;
            }

            var projectLanguage = projectInfo.RequiredElement(ns + "ProjectLanguage").Value;
            var sonarLanguage = projectLanguage == "C#" ? "cs" : "vbnet";
            var analysisSettings = projectInfo.RequiredElement(ns + "AnalysisSettings");
            var reportFilePathsNameAttribute = $"sonar.{sonarLanguage}.roslyn.reportFilePaths";
            var projectOutPathsNameAttribute = $"sonar.{sonarLanguage}.analyzer.projectOutPaths";

            var reportFilePathsProperty = 
                analysisSettings
                .Elements()
                .FirstOrDefault(p => p.Attribute("Name")?.Value == reportFilePathsNameAttribute);

            if (reportFilePathsProperty != null)
            {
                var reportFilePaths = 
                    reportFilePathsProperty.Value
                    .Split("|").Select(x => x.Trim()).Where(x => !string.IsNullOrEmpty(x)).ToHashSet();
                reportFilePaths.Add(reSharperRoslynFile);
                reportFilePathsProperty.Value = string.Join("|", reportFilePaths);
            } else
            {
                reportFilePathsProperty = new XElement(ns + "Property", reSharperRoslynFile);
                reportFilePathsProperty.SetAttributeValue("Name", reportFilePathsNameAttribute);
                analysisSettings.Add(reportFilePathsProperty);
            }

            var projectOutPathsProperty =
                analysisSettings
                .Elements()
                .FirstOrDefault(p => p.Attribute("Name")?.Value == projectOutPathsNameAttribute);

            if (projectOutPathsProperty != null)
            {
                projectOutPathsProperty.Value = projectInfoFile.DirectoryName ?? string.Empty;
            } else
            {
                projectOutPathsProperty = new XElement(ns + "Property", projectInfoFile.DirectoryName);
                projectOutPathsProperty.SetAttributeValue("Name", projectOutPathsNameAttribute);
                analysisSettings.Add(projectOutPathsProperty);
            }

            projectInfo.Save(projectInfoFile.FullName);

            Logger.Information(
                $"{reSharperRoslynFile} is successfully added to {projectInfoFile.FullName}.");
        }
    }
}
