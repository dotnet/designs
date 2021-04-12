@echo off
dotnet run --project %~dp0\tools\update-index -- %~dp0 %*
