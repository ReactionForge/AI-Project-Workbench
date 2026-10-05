using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Workbench;
public class GitFile {
 public string Path {get;set;}="";
 public string State {get;set;}="";
 public string Index {get;set;}="";
 public string WorkingSha256 {get;set;}="";
}
public class GitFacts {
 public string State {get;set;}="unknown";
 public string Branch {get;set;}="unknown";
 public string Commit {get;set;}="unknown";
 public string Scope {get;set;}="";
 public string Detail {get;set;}="";
 public List<GitFile> Files {get;set;}=new();
 public string Fingerprint()=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(this))));
 public string Describe()=> "Git："+State+"；分支："+Branch+"；提交："+Commit+"\n只读范围："+Scope+"\n"+Detail+"\n"+(Files.Count==0?"范围内无文件记录":string.Join("\n",Files.Select(f=>f.State+" "+f.Path+"；工作文件="+f.WorkingSha256+"；index="+f.Index)));
}
public partial class Workspace {
 internal static (int Code,string Output) GitProcess(string directory,string root,params string[] arguments){
  var psi=new ProcessStartInfo("git"){WorkingDirectory=directory,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  foreach(var key in psi.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())psi.Environment.Remove(key);
  psi.Environment["GIT_CONFIG_NOSYSTEM"]="1";psi.Environment["GIT_CONFIG_SYSTEM"]="NUL";psi.Environment["GIT_CONFIG_GLOBAL"]="NUL";
  psi.Environment["GIT_OPTIONAL_LOCKS"]="0";psi.Environment["GIT_TERMINAL_PROMPT"]="0";psi.Environment["GIT_NO_LAZY_FETCH"]="1";
  psi.Environment["HOME"]=Path.Combine(root,"runs","git-home");psi.Environment["TEMP"]=Path.Combine(root,"runs","temp");psi.Environment["TMP"]=psi.Environment["TEMP"];
  foreach(var a in new[]{"--no-optional-locks","-c","core.fsmonitor=false","-c","core.untrackedCache=false","-c","core.preloadIndex=false","-c","core.quotePath=false","-c","core.hooksPath="+Path.Combine(root,"runs","disabled-git-hooks")})psi.ArgumentList.Add(a);
  foreach(var a in arguments)psi.ArgumentList.Add(a);
  using var process=Process.Start(psi)??throw new InvalidOperationException("Git不可用");
  var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
  if(!process.WaitForExit(10000)){process.Kill(true);throw new IOException("Git只读检查超时");}
  System.Threading.Tasks.Task.WaitAll(output,error);
  if(output.Result.Length>2000000)throw new IOException("Git输出超过本阶段上限");
  return(process.ExitCode,output.Result);
 }
 void CheckGitMetadata(string id){
  var path=ProjectPath(id,"repo/.git");
  if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Git根包含链接");
  var pending=new Stack<string>();pending.Push(path);
  int count=0;
  while(pending.Count>0){
   var current=pending.Pop();
   foreach(var child in Directory.EnumerateFileSystemEntries(current)){
    if(++count>20000)throw new IOException("Git元数据范围过大");
    if((File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new IOException("Git元数据包含链接");
    if(Directory.Exists(child))pending.Push(child);
   }
  }
  foreach(var f in new[]{"commondir","gitdir","objects/info/alternates","objects/info/http-alternates","config.worktree"})
   if(File.Exists(ProjectPath(id,"repo/.git/"+f)))throw new IOException("共享/外接Git元数据未开放");
  var config=ProjectPath(id,"repo/.git/config");
  if(File.Exists(config)){
   if(new FileInfo(config).Length>262144)throw new IOException("Git配置过大");
   if(Regex.IsMatch(File.ReadAllText(config),@"(?im)^\s*\[\s*(include(If)?|filter)\b"))throw new IOException("Git配置包含未开放");
  }
 }
 public GitFacts ReadGit(ProjectRecord p,WorkTask t){
  string git=ProjectPath(p.Id,"repo/.git");
  if(File.Exists(git))return new GitFacts{State="unsupported",Detail="外接.git文件被拒绝，未跟随引用"};
  if(!Directory.Exists(git))return new GitFacts{State="not-configured",Detail="本项目repo未设置独立Git；未搜索父目录"};
  try{
   var config=ProjectPath(p.Id,"repo/.git/config");using var configReadLock=File.Exists(config)?new FileStream(config,FileMode.Open,FileAccess.Read,FileShare.Read):null;
   CheckGitMetadata(p.Id);
   var first=CaptureGit(p,t);var second=CaptureGit(p,t);
   if(first.Fingerprint()!=second.Fingerprint())return new GitFacts{State="changed-during-read",Detail="读取期间来源发生变化，重新生成"};
   return second;
  }catch{return new GitFacts{State="unavailable",Detail="Git不可用、路径不安全或状态超出支持范围；未视为干净"};}
 }
 GitFacts CaptureGit(ProjectRecord p,WorkTask t){
  var repo=ProjectPath(p.Id,"repo");var git=ProjectPath(p.Id,"repo/.git");
  (int Code,string Output) Run(params string[] args)=>GitProcess(repo,Root,new[]{"--git-dir="+git,"--work-tree="+repo}.Concat(args).ToArray());
  var top=Run("rev-parse","--show-toplevel");if(top.Code!=0||!string.Equals(Path.GetFullPath(top.Output.Trim()),repo,StringComparison.OrdinalIgnoreCase))throw new IOException("Git根不匹配");
  var branch=Run("symbolic-ref","--quiet","--short","HEAD");var commit=Run("rev-parse","--verify","HEAD^{commit}");
  if(branch.Code!=0&&branch.Code!=1)throw new IOException("分支未知");
  if(commit.Code!=0&&(branch.Code!=0||Run("show-ref","--verify","--quiet","refs/heads/"+branch.Output.Trim()).Code!=1))throw new IOException("提交缺失或损坏");
  string commitId=commit.Code==0?commit.Output.Trim():"unborn";
  if(commit.Code==0&&!Regex.IsMatch(commitId,"^[a-f0-9]{40,64}$"))throw new IOException("Git提交无效");
  var paths=new List<string>();
  foreach(var relative in t.RelatedFiles){
   var n=NormalizeRelated(relative);if(n.StartsWith("repo/",StringComparison.OrdinalIgnoreCase)){ProjectPath(p.Id,n);paths.Add(n[5..]);}
  }
  string scope=t.Scope.Replace('\\','/').TrimEnd('/');
  if(scope=="repo")paths.Add("");
  else if(scope.StartsWith("repo/",StringComparison.OrdinalIgnoreCase)){
   NormalizeRelated(scope);ProjectPath(p.Id,scope);paths.Add(scope[5..]);
  }
  paths=paths.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x=>x,StringComparer.Ordinal).ToList();
  var result=new GitFacts{State=commitId=="unborn"?"unborn":"observed",Branch=branch.Code==0?branch.Output.Trim():"detached",Commit=commitId,Scope=paths.Count==0?"仅提交/分支；未登记repo范围":string.Join("、",paths.Select(x=>"repo/"+x)),Detail="工作台仅读取，未提交/推送/切换/清理；包含登记范围内被Git忽略以外的文件"};
  if(paths.Count==0)return result;
  var specs=paths.Select(x=>":(literal)"+x).ToArray();
  var status=Run(new[]{"status","--porcelain=v1","-z","--untracked-files=all","--ignore-submodules=all","--"}.Concat(specs).ToArray());
  var index=Run(new[]{"ls-files","--stage","-z","--"}.Concat(specs).ToArray());
  if(status.Code!=0||index.Code!=0)throw new IOException("Git状态不可读");
  var records=new Dictionary<string,GitFile>(StringComparer.OrdinalIgnoreCase);
  foreach(var row in index.Output.Split('\0',StringSplitOptions.RemoveEmptyEntries)){
   int tab=row.IndexOf('\t');if(tab<0)throw new IOException("Git索引无效");
   var meta=row[..tab].Split(' ');var relative=row[(tab+1)..];
   if(meta.Length!=3||meta[0]=="160000"||meta[0]=="120000")throw new IOException("子模块/链接或索引不支持");
   if(!records.TryGetValue(relative,out var f)){f=new GitFile{Path=relative,State="clean"};records.Add(relative,f);}
   f.Index+=meta[0]+":"+meta[1]+":"+meta[2]+";";
  }
  var entries=status.Output.Split('\0',StringSplitOptions.RemoveEmptyEntries);
  for(int i=0;i<entries.Length;i++){
   var row=entries[i];if(row.Length<4||row[2]!=' ')throw new IOException("Git状态格式无效");
   string state=row[..2],relative=row[3..];
   if(!records.TryGetValue(relative,out var f)){f=new GitFile{Path=relative};records.Add(relative,f);}
   f.State=state;
   if(state.Contains('R')||state.Contains('C')){if(++i>=entries.Length)throw new IOException("重命名来源缺失");NormalizeArchiveName(entries[i]);f.State+=" <- "+entries[i];}
  }
  if(records.Count>5000)throw new IOException("受影响文件过多");
  foreach(var f in records.Values.OrderBy(x=>x.Path,StringComparer.Ordinal)){
   var relative=NormalizeArchiveName("repo/"+f.Path);
   if(ExcludedAssetPath(relative)!=""){f.WorkingSha256="EXCLUDED_SENSITIVE_OR_CACHE";result.Files.Add(f);continue;}
   var full=ProjectPath(p.Id,relative);
   f.WorkingSha256=File.Exists(full)?Hash(File.ReadAllBytes(full)):"MISSING";
   result.Files.Add(f);
  }
  return result;
 }
 public string GitFingerprint(ProjectRecord p,WorkTask t)=>ReadGit(p,t).Fingerprint();
}
