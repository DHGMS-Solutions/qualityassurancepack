//////////////////////////////////////////////////////////////////////
// MODULES
//////////////////////////////////////////////////////////////////////
#module "nuget:?package=Cake.DotNetTool.Module"

//////////////////////////////////////////////////////////////////////
// ADDINS
//////////////////////////////////////////////////////////////////////

//#addin "nuget:?package=Cake.FileHelpers&version=3.0.0"
#addin "nuget:?package=Cake.Coveralls&version=0.9.0"
//#addin "nuget:?package=Cake.PinNuGetDependency&version=3.0.1"
//#addin "nuget:?package=Cake.Powershell&version=0.4.5"
//#addin "nuget:?package=Cake.Sonar&version=1.0.4"

//////////////////////////////////////////////////////////////////////
// TOOLS
//////////////////////////////////////////////////////////////////////

#tool "nuget:?package=GitReleaseManager&version=0.7.1"
#tool "nuget:?package=coveralls.io&version=1.4.2"
#tool "nuget:?package=OpenCover&version=4.6.519"
#tool "nuget:?package=ReportGenerator&version=3.1.2"
#tool "nuget:?package=vswhere&version=2.5.2"
#tool "nuget:?package=xunit.runner.console&version=2.4.0"
#tool "nuget:?package=GitVersion.CommandLine&version=3.6.5"
#tool "nuget:?package=MSBuild.SonarQube.Runner.Tool"
#tool "dotnet:?package=dotnet-sonarscanner&version=4.4.2"

//////////////////////////////////////////////////////////////////////
// ARGUMENTS
//////////////////////////////////////////////////////////////////////

var target = Argument("target", "Default");
if (string.IsNullOrWhiteSpace(target))
{
    target = "Default";
}

//////////////////////////////////////////////////////////////////////
// PREPARATION
//////////////////////////////////////////////////////////////////////

// Should MSBuild treat any errors as warnings?
var treatWarningsAsErrors = false;

// Build configuration
var githubOwner = "dhgms-solutions";
var githubRepository = "qualityassurancepack";
var githubUrl = string.Format("https://github.com/{0}/{1}", githubOwner, githubRepository);
var solutionToBuild = "./src/Dhgms.QualityAssurancePack.sln";

var local = BuildSystem.IsLocalBuild;
var isPullRequest = AppVeyor.Environment.PullRequest.IsPullRequest;
var isRepository = StringComparer.OrdinalIgnoreCase.Equals(githubOwner + "/" + githubRepository, AppVeyor.Environment.Repository.Name);

var isDevelopBranch = StringComparer.OrdinalIgnoreCase.Equals("develop", AppVeyor.Environment.Repository.Branch);
var isReleaseBranch = StringComparer.OrdinalIgnoreCase.Equals("master", AppVeyor.Environment.Repository.Branch);
var isTagged = AppVeyor.Environment.Repository.Tag.IsTag;
var unitTestProjectFilePath = "";


var vsPath = VSWhereLatest();

if (vsPath == null)
{
    throw new Exception("Unable to find Visual Studio");
}
Information("Visual Studio Path: " + vsPath);

var msBuildPath = GetFiles(vsPath + "/**/msbuild.exe").FirstOrDefault();
if (msBuildPath == null)
{
    throw new Exception("Unable to find MSBuild path");
}
Information("MSBuild Path: " + msBuildPath);

var androidHome = EnvironmentVariable("ANDROID_HOME");

// Version
var gitVersion = GitVersion();
var majorMinorPatch = gitVersion.MajorMinorPatch;
var informationalVersion = gitVersion.InformationalVersion;
var nugetVersion = gitVersion.NuGetVersionV2;
var buildVersion = gitVersion.FullBuildMetaData;
var assemblyVersion = gitVersion.Major + "." + gitVersion.Minor + ".0.0";
var fileVersion = majorMinorPatch;
var versionSuffix = gitVersion.PreReleaseTag;
var packageVersion = nugetVersion;
Information("nugetVersion: " + nugetVersion);
Information("informationalVersion: " + informationalVersion);
Information("assemblyVersion: " + assemblyVersion);
Information("fileVersion: " + fileVersion);


// Artifacts
var artifactDirectory = "./artifacts/";
var testCoverageOutputFile = artifactDirectory + "OpenCover.xml";
var packageWhitelist = new[] { "Dhgms.QualityAssurancePack" };

var runSonarQube = false;
var sonarQubePreview = false;
var sonarQubeLogin = EnvironmentVariable("sonarqubeLogin");
var sonarqubeProjectKey = "qualityassurancepack";
var sonarqubeOrganisationKey = "dpvreony-github";

// sonarqube
if (isRepository && !local && sonarQubeLogin != null)
{
    if (isPullRequest)
    {
        // currently doesn't happen as PR's don't pass secure env vars
        sonarQubePreview = true;
        runSonarQube = true;
        Information("Sonar on PR " + AppVeyor.Environment.PullRequest.Number);
    }
    else
    {
        runSonarQube = true;
        Information("Sonar on branch " + AppVeyor.Environment.Repository.Branch);
    }
}


// Define global marcos.
Action Abort = () => { throw new Exception("a non-recoverable fatal error occurred."); };

///////////////////////////////////////////////////////////////////////////////
// SETUP / TEARDOWN
///////////////////////////////////////////////////////////////////////////////
Setup(context =>
{
    Information("Building version {0}. (isTagged: {1})", informationalVersion, isTagged);

    CreateDirectory(artifactDirectory);
});

Teardown(context =>
{
    // Executed AFTER the last task.
});


Task("BuildSolution")
    .IsDependentOn("SonarBegin")
    .Does (() =>
{
    Action<string> build = (solution) =>
    {
        Information("Building {0} using {1}", solution, msBuildPath);

        MSBuild(solution, new MSBuildSettings() {
                ToolPath = msBuildPath,
                ArgumentCustomization = args => args.Append("/bl:build.binlog /m")
            }
            .WithTarget("build")
            .WithProperty("PackageOutputPath",  MakeAbsolute(Directory(artifactDirectory)).ToString().Quote())
            .WithProperty("TreatWarningsAsErrors", treatWarningsAsErrors.ToString())
            .SetConfiguration("Release")
            .WithProperty("Version", packageVersion)
            .WithProperty("AssemblyVersion", assemblyVersion)
            .WithProperty("FileVersion", fileVersion)
            .WithProperty("InformationalVersion", informationalVersion)
            .WithProperty("VersionSuffix", versionSuffix)
            .WithProperty("PublishRepositoryUrl", "true")
            .WithProperty("EmbedUntrackedSources", "true")
            .WithProperty("AllowedOutputExtensionsInPackageBuildOutputFolder", "$(AllowedOutputExtensionsInPackageBuildOutputFolder);.pdb")
            .SetVerbosity(Verbosity.Minimal)
            .SetNodeReuse(false));
    };

	Information("Triggering Nuget Restore");
	Information("Solution to Build: " + solutionToBuild);
	Information("msBuildPath: " + msBuildPath);
	Information("nuget version: " + nugetVersion.ToString());
    // Restore must be a separate step
    MSBuild(solutionToBuild, new MSBuildSettings() {
            ToolPath = msBuildPath,
            ArgumentCustomization = args => args.Append("/bl:restore.binlog /m")
        }
        .WithTarget("restore")
        .WithProperty("Version", nugetVersion.ToString())
        .SetVerbosity(Verbosity.Minimal));
    
	Information("Triggering Build");
    build(solutionToBuild);
});

// https://andrewlock.net/running-tests-with-dotnet-xunit-using-cake/
// https://github.com/sawilde/opencover/wiki/Usage
// debug type and symbols are set in projects due to an issue in opencover and new pdb format
// https://github.com/OpenCover/opencover/wiki/Troubleshooting-%22Cannot-instrument-assembly%22
Task("RunUnitTests")
    .IsDependentOn("BuildSolution")
    .Does(() =>
{
    var projectDirectory = new FilePath(unitTestProjectFilePath).GetDirectory();
    var pdbDirectory = projectDirectory + "\\bin\\Debug\\netcoreapp2.0";

    // workaround for https://github.com/xunit/xunit/issues/1573
    // C:\Program Files\dotnet\shared\Microsoft.NETCore.App
    var fxVersion = "2.0.5";

	Action<ICakeContext> testAction = tool => {
        tool.DotNetCoreTool(
                projectPath: unitTestProjectFilePath,
                command: "test",
                arguments: "-- -noshadow -fxversion " + fxVersion + " -configuration Debug -diagnostics"
            );
    };

    OpenCover(testAction,
        testCoverageOutputFile,
        new OpenCoverSettings {
            //LogLevel = OpenCoverLogLevel.All,
            MergeOutput = true,
            Register = "user",
            ReturnTargetCodeOffset = 0,
            ArgumentCustomization = args => args.Append("-coverbytest:*.UnitTests.dll").Append("-searchdirs:" + pdbDirectory).Append("-oldstyle"),
            // working dir set to allow use of dotnet-xunit
            WorkingDirectory = projectDirectory
        }
        .WithFilter("+[NLog*]*")
        .ExcludeByAttribute("*.ExcludeFromCodeCoverage*")
        .ExcludeByFile("*/*Designer.cs")
        .ExcludeByFile("*/*.g.cs")
        .ExcludeByFile("*/*.g.i.cs"));

    ReportGenerator(testCoverageOutputFile, artifactDirectory);
}).ReportError(exception =>
{
    var apiApprovals = GetFiles("./**/ApiApprovalTests.*");
    CopyFiles(apiApprovals, artifactDirectory);
});

Task("UploadTestCoverage")
    .WithCriteria(() => !local)
    .WithCriteria(() => isRepository)
    .WithCriteria(() => !isPullRequest)
    //.IsDependentOn("RunUnitTests")
    .Does(() =>
{
    // Resolve the API key.
    // pull requests can't currently get secure variables
    var token = EnvironmentVariable("COVERALLS_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The COVERALLS_TOKEN environment variable is not defined.");
    }

    CoverallsIo(testCoverageOutputFile, new CoverallsIoSettings()
    {
        RepoToken = token
    });
});

Task("Sonar")
  .IsDependentOn("SonarBegin")
  .IsDependentOn("BuildSolution")
  //.IsDependentOn("RunUnitTests")
  //.IsDependentOn("UploadTestCoverage")
  .IsDependentOn("SonarEnd");
  
Task("SonarBegin")
  .WithCriteria(() => runSonarQube)
  .Does(() => {
    var coverageFilePath = MakeAbsolute(new FilePath(testCoverageOutputFile)).FullPath;
    Information("Sonar: Test Coverage Output File: " + testCoverageOutputFile);
    var arguments = "sonarscanner begin /k:\"" + sonarqubeProjectKey + "\" /v:\"" + nugetVersion + "\" /d:\"sonar.host.url=https://sonarcloud.io\" /d:\"sonar.organization=" + sonarqubeOrganisationKey + "\" /d:\"sonar.login=" + sonarQubeLogin + "\" /d:sonar.cs.opencover.reportsPaths=\"" + coverageFilePath + "\"";

    if (sonarQubePreview) {
        Information("Sonar: Running Sonar on PR " + AppVeyor.Environment.PullRequest.Number);
        arguments += " /d:\"sonar.projectVersion=sonar.projectVersion\" /d:\"sonar.analysis.mode=preview\"";
    }
    else {
        Information("Sonar: Running Sonar on branch " + AppVeyor.Environment.Repository.Branch);
        if (!isReleaseBranch)
        {
            arguments += " /d:\"sonar.branch.name=" + AppVeyor.Environment.Repository.Branch + "\"";
        }
    }

    StartProcess("dotnet.exe", "tool install --global dotnet-sonarscanner");
    var sonarStartSettings = new ProcessSettings{ Arguments = arguments };
    StartProcess("dotnet.exe", sonarStartSettings);
  });

Task("SonarEnd")
  //.IsDependentOn("UploadTestCoverage")
  .WithCriteria(() => runSonarQube)
  .Does(() => {
    var sonarEndSettings = new ProcessSettings{ Arguments = "sonarscanner end /d:\"sonar.login=" + sonarQubeLogin + "\"" };
    StartProcess("dotnet.exe", sonarEndSettings);
  });

Task("Package")
    .IsDependentOn("Sonar")
    //.IsDependentOn("RunUnitTests")
    //.IsDependentOn("PinNuGetDependencies")
    .Does (() =>
{
    Information("Packaging");

    MSBuild(solutionToBuild, new MSBuildSettings() {
            ToolPath = msBuildPath,
            ArgumentCustomization = args => args.Append("/bl:pack.binlog /m")
        }
        .WithTarget("pack")
        .WithProperty("PackageOutputPath",  MakeAbsolute(Directory(artifactDirectory)).ToString().Quote())
        .WithProperty("TreatWarningsAsErrors", treatWarningsAsErrors.ToString())
        .SetConfiguration("Release")
        .WithProperty("Version", packageVersion)
        .WithProperty("AssemblyVersion", assemblyVersion)
        .WithProperty("FileVersion", fileVersion)
        .WithProperty("InformationalVersion", informationalVersion)
        .WithProperty("VersionSuffix", versionSuffix)
        .SetVerbosity(Verbosity.Minimal)
        .SetNodeReuse(false));
});

Task("PublishPackages")
    //.IsDependentOn("RunUnitTests")
    .IsDependentOn("Package")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isRepository)
    .WithCriteria(() => isDevelopBranch || isReleaseBranch)
    .Does (() =>
{

    if (isReleaseBranch && !isTagged)
    {
        Information("Packages will not be published as this release has not been tagged.");
        return;
    }

    // Resolve the API key.
    var apiKey = EnvironmentVariable("NUGET_APIKEY");
    if (string.IsNullOrEmpty(apiKey))
    {
        throw new Exception("The NUGET_APIKEY environment variable is not defined.");
    }

    var source = EnvironmentVariable("NUGET_SOURCE");
    if (string.IsNullOrEmpty(source))
    {
        throw new Exception("The NUGET_SOURCE environment variable is not defined.");
    }

    // only push whitelisted packages.
    foreach(var package in packageWhitelist)
    {
        // only push the package which was created during this build run.
        var packagePath = artifactDirectory + File(string.Concat(package, ".", nugetVersion, ".nupkg"));

        // Push the package.
        NuGetPush(packagePath, new NuGetPushSettings {
            Source = source,
            ApiKey = apiKey
        });
    }
});

Task("CreateRelease")
    .IsDependentOn("Package")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isRepository)
    .WithCriteria(() => isReleaseBranch)
    .WithCriteria(() => !isTagged)
    .Does (() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    GitReleaseManagerCreate(username, token, githubOwner, githubRepository, new GitReleaseManagerCreateSettings {
        Milestone         = majorMinorPatch,
        Name              = majorMinorPatch,
        Prerelease        = true,
        TargetCommitish   = "master"
    });
});

Task("PublishRelease")
    //.IsDependentOn("RunUnitTests")
    .IsDependentOn("Package")
    .WithCriteria(() => !local)
    .WithCriteria(() => !isPullRequest)
    .WithCriteria(() => isRepository)
    .WithCriteria(() => isReleaseBranch)
    .WithCriteria(() => isTagged)
    .Does (() =>
{
    var username = EnvironmentVariable("GITHUB_USERNAME");
    if (string.IsNullOrEmpty(username))
    {
        throw new Exception("The GITHUB_USERNAME environment variable is not defined.");
    }

    var token = EnvironmentVariable("GITHUB_TOKEN");
    if (string.IsNullOrEmpty(token))
    {
        throw new Exception("The GITHUB_TOKEN environment variable is not defined.");
    }

    // only push whitelisted packages.
    foreach(var package in packageWhitelist)
    {
        // only push the package which was created during this build run.
        var packagePath = artifactDirectory + File(string.Concat(package, ".", nugetVersion, ".nupkg"));

        GitReleaseManagerAddAssets(username, token, githubOwner, githubRepository, majorMinorPatch, packagePath);
    }

    GitReleaseManagerClose(username, token, githubOwner, githubRepository, majorMinorPatch);
});

//////////////////////////////////////////////////////////////////////
// TASK TARGETS
//////////////////////////////////////////////////////////////////////

Task("Default")
    //.IsDependentOn("CreateRelease")
    .IsDependentOn("PublishPackages")
    //.IsDependentOn("PublishRelease")
    .Does (() =>
{
});

//////////////////////////////////////////////////////////////////////
// EXECUTION
//////////////////////////////////////////////////////////////////////

RunTarget(target);
