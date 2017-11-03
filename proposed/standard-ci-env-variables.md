# Standardized Environment Variables for CI Services

Many CI services set environment variables that can be used by developer builds. Many of the environment variables are common in scope and theme across CI services, however, the specific variable names are different. We need a set of standardized environment variables across CI services so that an application platform like .NET can rely on them, and provide a common user experience across CI services.

## Context

The .NET Core SDK is implementing new scenarios that require source control manager information. An initial idea was to call out to specific source control tools to get this information. This approach is problematic since it requires an implementation for each source control manager and it has the potential to be fragile and slow.

It turns out that CI services all of the information that these new .NET Core scenarios need via environment variables. Also, there is a strong relationship between these scenarios and official CI-provided builds (much less on local developer builds). As a result, relying on the CI-provided environment variables is attractive.

To make it possible to provide application platform provided experiences across CI services, we need a standardized set of environment variables that are supported across those same CI services. Ideally, this set of environment variables would be supported across multiple CI services and useful for multiple application environments, not just .NET.

## Proposed environment variables

The .NET Core SDK needs are oriented around source control. As a result, the intial list is source control oriented, but there is no affinity to source control on the general idea of standardized environment variables.

It is important that these environment variables do not conflict with other variables. To avoid that, all environment variables will be prepended with "STANDARDCI-". This name is a first proposal for the idea and it may get changed based on feedback.

* **STANDARDCI\_REPOSITORYCOMMITID** -- Commit hash / ID; Example: 2ba93796dcf132de447886d4d634414ee8cb069d
* **STANDARDCI\_REPOSITORYROOT** -- Root of repository; Example: D:\repos\corefx
* **STANDARDCI\_REPOSITORYNAME** -- Name repository; Example: dotnet\corefx
* **STANDARDCI\_REPOSITORYURI** -- Uri for repository; Example: https://github.com/dotnet/corefx

## Support from CI Services

This plan will only work if CI services decide to support these environment variables. An important question is whether CI services have similar environment variables already. The table below suggests that the information we need is already available. An arbitrary sample of CI services were picked for this exercise.

| Environment Variable | VSTS | Travis CI| AppVeyor | Circle CI | AWS CodeBuild |
| -------------------- | ---- | -------- | -------- | --------- | ------------- |
|STANDARDCI\_REPOSITORYCOMMITID | BUILD\_SOURCEVERSION | TRAVIS\_COMMIT |APPVEYOR\_REPO\_COMMIT | CIRCLE\_SHA1 | CODEBUILD_RESOLVED_SOURCE_VERSION |
|STANDARDCI\_REPOSITORYROOT|BUILD\_REPOSITORY\_LOCALPATH|TRAVIS\_BUILD\_DIR| APPVEYOR\_BUILD\_FOLDER | CIRCLE\_WORKING\_DIRECTORY | CODEBUILD_SRC_DIR |
|STANDARDCI\_REPOSITORYNAME|BUILD\_REPOSITORY\_NAME| TRAVIS\_REPO\_SLUG | APPVEYOR\_REPO\_NAME | CIRCLE\_PROJECT\_USERNAME + CIRCLE\_PROJECT\_REPONAME |
|STANDARDCI\_REPOSITORYURI|BUILD\_REPOSITORY\_URI| | | CIRCLE\_REPOSITORY\_URL | CODEBUILD_SOURCE_REPO_URL |

The [VSTS](https://www.visualstudio.com/team-services/) team has graciously agreed to publish environment variables in the proposed STANDARDCI format.

* [VSTS](https://www.visualstudio.com/team-services/) -- [environment variables](https://docs.microsoft.com/en-us/vsts/build-release/concepts/definitions/build/variables?tabs=batch#predefined-variables)
* [Travis CI](https://travis-ci.org/) -- [environment variables](https://docs.travis-ci.com/user/environment-variables/#Default-Environment-Variables)
* [AppVeyor](https://www.appveyor.com/) -- [environment variables](https://www.appveyor.com/docs/environment-variables/)
* [Circle CI](https://circleci.com) -- [environment variables](https://circleci.com/docs/2.0/env-vars)

## Plan

We plan to do the following:

* Get general feedback.
* Reach out to CI service providers, get their feedback and ask them to implement this spec.
