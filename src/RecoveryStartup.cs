using System;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Workbench;
public class RecoveryMarker {
 public string Schema{get;set;}="workbench-recovery-copy-v1";
 public string ProjectId{get;set;}="";
 public bool Demo{get;set;}
 public string OriginSourceStamp{get;set;}="";
 public string OriginArchiveSha256{get;set;}="";
}
public static class RecoveryStartup {
 public static Workspace Resolve(string baseRoot,string candidate){
  string parent=Path.GetFullPath(Path.Combine(baseRoot,"runs","restored"));
  string target=Path.GetFullPath(candidate);
  if(!string.Equals(Path.GetDirectoryName(target),parent,StringComparison.OrdinalIgnoreCase)||!Regex.IsMatch(Path.GetFileName(target),"^restore-[a-f0-9]{32}$"))throw new InvalidOperationException("仅能打开本工作区runs/restored内的独立恢复副本");
  var guard=new Workspace(baseRoot);var relative=Path.GetRelativePath(baseRoot,target);
  var markerPath=guard.Safe(relative+"/recovery.json");var projectMarker=guard.Safe(relative+"/project.json");
  if(!File.Exists(markerPath)||!File.Exists(projectMarker))throw new InvalidDataException("恢复副本标记缺失，未打开");
  var marker=JsonSerializer.Deserialize<RecoveryMarker>(File.ReadAllText(markerPath))??throw new InvalidDataException("恢复标记无效");
  if(marker.Schema!="workbench-recovery-copy-v1"||!Regex.IsMatch(marker.ProjectId,"^P-[a-f0-9]{12}$")||!Regex.IsMatch(marker.OriginArchiveSha256,"^[A-F0-9]{64}$"))throw new InvalidDataException("恢复身份标记无效");
  var result=new Workspace(target,marker.Demo);result.Load(marker.ProjectId);return result;
 }
}
