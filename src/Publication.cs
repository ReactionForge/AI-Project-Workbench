using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace Workbench;
public class PublishFile {
 public string Path {get;set;}="";
 public string Status {get;set;}="clean";
 public string IndexBlob {get;set;}="";
 public string WorkingSha256 {get;set;}="";
 public long Bytes {get;set;}
 public List<string> Risks {get;set;}=new();
 public string Diff {get;set;}="";
}
public class PublishEvidence {
 public string Kind {get;set;}="";
 public string Path {get;set;}="";
 public string Sha256 {get;set;}="";
 public string AssociatedCommit {get;set;}="unknown";
 public string SourceFingerprint {get;set;}="";
 public string Verdict {get;set;}="unverified_manual_association";
}
public class PublishReport {
 public List<PublishEvidence> Evidence {get;set;}=new();
 public List<string> UnpushedCommits {get;set;}=new();
 public string CommittedDiff {get;set;}="";
 public string Subject {get;set;}="";
 public string ObservedAt {get;set;}="";
 public string Branch {get;set;}="unknown";
 public string Commit {get;set;}="unknown";
 public string Worktree {get;set;}="unknown";
 public string LastRemoteSha {get;set;}="";
 public string LastRemoteObservedAt {get;set;}="";
 public string LastVerifiedAccount {get;set;}="";
 public string RemoteState {get;set;}="unknown";
 public string RemoteSha {get;set;}="unknown";
 public string RemoteObservedAt {get;set;}="";
 public string Repository {get;set;}="";
 public string Account {get;set;}="unverified";
 public string Visibility {get;set;}="unknown";
 public string RemoteDetail {get;set;}="尚未联网核对；不推断同步";
 public string Stamp {get;set;}="";
 public string MetadataHash {get;set;}="";
 public List<PublishFile> Files {get;set;}=new();
 public List<string> Warnings {get;set;}=new();
}
public class PublishBinding {
 public string Subject {get;set;}="";
 public string Repository {get;set;}="";
 public string Branch {get;set;}="";
 public string Account {get;set;}="";
 public string Visibility {get;set;}="";
 public string MetadataHash {get;set;}="";
 public string ConfirmedAt {get;set;}="";
}
public class PublishPlan {
 public string Kind {get;set;}="readonly-review-plan";
 public bool CanExecute {get;set;}=false;
 public string State {get;set;}="review_required";
 public string ExportedAt {get;set;}="";
 public PublishReport Report {get;set;}=new();
 public PublishBinding? Binding {get;set;}
 public List<PublishFile> SelectedFiles {get;set;}=new();
 public string CandidateCommit {get;set;}="unknown";
 public string SourceFingerprint {get;set;}="";
 public string TestEvidenceSha256 {get;set;}="not_registered";
 public string PackageSha256 {get;set;}="not_registered";
 public string ExternalKeyboardAndDpi {get;set;}="not_run";
 public string GithubPush {get;set;}="not_run";
 public string MainMerge {get;set;}="not_run";
 public string ReleasePublication {get;set;}="not_run";
}
public sealed class PublicationService {
 readonly Workspace w;
 public PublicationService(Workspace workspace){w=workspace;}
 public string Repo(string subject){
  if(subject=="self")return w.Safe("repo");
  if(!Regex.IsMatch(subject,@"^(local|examples)/P-[a-f0-9]{12}$"))throw new IOException("发布范围无效");
  if(!subject.StartsWith(w.Mode+"/",StringComparison.Ordinal))throw new IOException("拒绝跨模式读取");
  return w.Safe("data/"+subject.Split('/')[0]+"/Projects/"+subject.Split('/')[1]+"/repo");
 }
 string Store(string subject,string name){Repo(subject);var dir=w.Safe("runs/publication/"+subject.Replace('/','-'));Directory.CreateDirectory(dir);return w.Safe("runs/publication/"+subject.Replace('/','-')+"/"+name);}
 static string Hash(byte[] bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
 static string HashText(string s)=>Hash(Encoding.UTF8.GetBytes(s));
 static string SafeFile(string repo,string relative){
  if(string.IsNullOrWhiteSpace(relative)||relative.Contains('\\')||relative.Contains(':')||relative.Split('/').Any(x=>x==".."||x=="."||x==""))throw new IOException("文件路径无效");
  var path=Path.GetFullPath(Path.Combine(repo,relative));
  if(!path.StartsWith(repo+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new IOException("拒绝越界路径");
  var current=new FileInfo(path).Directory;
  while(current!=null&&current.FullName.StartsWith(repo,StringComparison.OrdinalIgnoreCase)){
   if(current.Exists&&(current.Attributes&FileAttributes.ReparsePoint)!=0)throw new IOException("拒绝链接目录");current=current.Parent;
  }
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("拒绝链接文件");
  return path;
 }
 static void Metadata(string repo){
  var git=SafeFile(repo,".git");if(File.Exists(git)||!Directory.Exists(git))throw new IOException("需要本项目独立的 .git 目录；不会发现父级仓库");
  var queue=new Stack<string>();queue.Push(git);int count=0;
  while(queue.Count>0)foreach(var child in Directory.EnumerateFileSystemEntries(queue.Pop())){
   if(++count>20000||(File.GetAttributes(child)&FileAttributes.ReparsePoint)!=0)throw new IOException("Git 元数据含链接或超过检查上限");if(Directory.Exists(child))queue.Push(child);
  }
  foreach(var name in new[]{"commondir","gitdir","objects/info/alternates","objects/info/http-alternates","config.worktree","info/grafts"})if(File.Exists(SafeFile(repo,".git/"+name)))throw new IOException("关联或共享 Git 元数据不支持");
  var config=SafeFile(repo,".git/config");if(new FileInfo(config).Length>262144||Regex.IsMatch(File.ReadAllText(config),@"(?im)^\s*\[\s*(include(If)?|filter)\b"))throw new IOException("Git 配置包含外部配置或过滤器");
 }
 internal async Task<(int Code,string Text)> Command(string file,string directory,string[] arguments,CancellationToken token){
  token.ThrowIfCancellationRequested();
  var psi=new ProcessStartInfo(file){WorkingDirectory=directory,UseShellExecute=false,RedirectStandardOutput=true,RedirectStandardError=true,CreateNoWindow=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8};
  foreach(var key in psi.Environment.Keys.Where(k=>k.StartsWith("GIT_",StringComparison.OrdinalIgnoreCase)).ToArray())psi.Environment.Remove(key);
  psi.Environment["GIT_CONFIG_NOSYSTEM"]="1";psi.Environment["GIT_CONFIG_GLOBAL"]="NUL";psi.Environment["GIT_CONFIG_SYSTEM"]="NUL";psi.Environment["GIT_OPTIONAL_LOCKS"]="0";psi.Environment["GIT_TERMINAL_PROMPT"]="0";psi.Environment["GIT_NO_LAZY_FETCH"]="1";psi.Environment["GIT_NO_REPLACE_OBJECTS"]="1";
  psi.Environment["GH_PROMPT_DISABLED"]="1";
  psi.Environment["TEMP"]=w.Safe("runs/temp");psi.Environment["TMP"]=psi.Environment["TEMP"];
  if(file=="git"){
   psi.Environment["HOME"]=w.Safe("runs/git-home");
   foreach(var a in new[]{"--no-optional-locks","-c","core.fsmonitor=false","-c","core.untrackedCache=false","-c","core.quotePath=false","-c","core.hooksPath="+w.Safe("runs/disabled-git-hooks")})psi.ArgumentList.Add(a);
  }
  foreach(var a in arguments)psi.ArgumentList.Add(a);
  using var limit=CancellationTokenSource.CreateLinkedTokenSource(token);limit.CancelAfter(TimeSpan.FromSeconds(file=="git"?15:30));
  using var process=Process.Start(psi)??throw new IOException("只读工具未启动");
  using var registration=limit.Token.Register(()=>{try{if(!process.HasExited)process.Kill(true);}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){}});
  async Task<string> Read(StreamReader reader){var text=new StringBuilder();var buffer=new char[4096];int n;while((n=await reader.ReadAsync(buffer.AsMemory(),limit.Token))>0){if(text.Length+n>2000000){limit.Cancel();throw new IOException("工具输出超过上限");}text.Append(buffer,0,n);}return text.ToString();}
  var output=Read(process.StandardOutput);var error=Read(process.StandardError);
  try{await Task.WhenAll(process.WaitForExitAsync(limit.Token),output,error);token.ThrowIfCancellationRequested();return(process.ExitCode,output.Result);}
  catch(OperationCanceledException)when(!token.IsCancellationRequested){throw new IOException("只读检查超时；结果未知");}
 }
 Task<(int Code,string Text)> Git(string repo,CancellationToken token,params string[] args)=>Command("git",repo,new[]{"--git-dir="+SafeFile(repo,".git"),"--work-tree="+repo}.Concat(args).ToArray(),token);
 public static string GithubTarget(string url){
  var match=Regex.Match(url,@"^(?:https://github\.com/|git@github\.com:|ssh://git@github\.com/)([A-Za-z0-9][A-Za-z0-9-]*)/([A-Za-z0-9_.-]+?)(?:\.git)?/?$",RegexOptions.IgnoreCase);
  return match.Success&&!new[]{".",".."}.Contains(match.Groups[2].Value)?match.Groups[1].Value+"/"+match.Groups[2].Value:"";
 }
 public static string RemoteRelation(string head,string remote,bool objectKnown,bool remoteAncestor,bool localAncestor){
  if(head==remote&&Regex.IsMatch(head,"^[a-f0-9]{40,64}$"))return "synced";
  if(!objectKnown)return "unknown";
  if(remoteAncestor)return "committed_unpushed";
  if(localAncestor)return "behind";
  return "diverged";
 }
 static string Redact(string text)=>Regex.Replace(text,@"(?im)^.*(?:api[_-]?key|access[_-]?token|password\s*[:=]|secret\s*[:=]|gh[pousr]_[A-Za-z0-9]|github_pat_|sk-[A-Za-z0-9]|BEGIN .{0,20}PRIVATE KEY|[A-Z]:[\\/]|/Users/|/home/|[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}).*$","[已隐藏：可能含凭据或私人信息]",RegexOptions.IgnoreCase);
 static void Scan(PublishFile f,string text){
  if(Redact(text)!=text&&!f.Risks.Contains("可能含凭据/私人路径/个人信息（保守规则）"))f.Risks.Add("可能含凭据/私人路径/个人信息（保守规则）");
  if(text.Contains('\0'))f.Risks.Add("二进制内容需单独人工审查");
 }
 static void PathRisk(PublishFile f){
  if(Regex.IsMatch(f.Path,@"(?i)(^|/)(\.env(?:\..*)?|credentials?[^/]*|[^/]*\.(?:pem|pfx|p12|key))$"))f.Risks.Add("敏感配置或密钥文件名");
  if(Regex.IsMatch(f.Path,@"(?i)(^|/)(node_modules|obj|bin|\.vs|\.idea|\.git|runs|releases|archive|data|inputs|screenshots|cache)(/|$)"))f.Risks.Add("数据/原始输入/运行证据/缓存/构建目录");
  if(f.Bytes>5*1024*1024)f.Risks.Add("超过本项目 5 MiB 审查阈值");
 }
 public async Task<PublishReport> Inspect(string subject,CancellationToken token){
  token.ThrowIfCancellationRequested();string repo=Repo(subject);Metadata(repo);
  using var configLock=new FileStream(SafeFile(repo,".git/config"),FileMode.Open,FileAccess.Read,FileShare.Read);
  Metadata(repo);
  var first=await Capture(subject,repo,token);var second=await Capture(subject,repo,token);
  if(first.Stamp!=second.Stamp)throw new IOException("检查期间源文件或 Git 状态变化，请重查");CarryRemoteCache(LoadReport(subject),second);return second;
 }
 async Task<PublishReport> Capture(string subject,string repo,CancellationToken token){
  async Task<string> Required(params string[] args){var result=await Git(repo,token,args);if(result.Code!=0)throw new IOException("Git 只读状态不可用");return result.Text;}
  var top=await Required("rev-parse","--show-toplevel");if(!string.Equals(Path.GetFullPath(top.Trim()),repo,StringComparison.OrdinalIgnoreCase))throw new IOException("Git 顶层范围不匹配");
  var branch=await Git(repo,token,"symbolic-ref","--quiet","--short","HEAD");var head=await Git(repo,token,"rev-parse","--verify","HEAD^{commit}");
  var r=new PublishReport{Subject=subject,ObservedAt=DateTime.UtcNow.ToString("O"),Branch=branch.Code==0?branch.Text.Trim():"detached",Commit=head.Code==0?head.Text.Trim():"unborn",MetadataHash=Hash(File.ReadAllBytes(SafeFile(repo,".git/config")))};
  if(head.Code==0&&!Regex.IsMatch(r.Commit,"^[a-f0-9]{40,64}$"))throw new IOException("提交标识不合法");
  if(r.Branch=="detached"||r.Commit=="unborn")r.Warnings.Add("游离 HEAD 或尚无提交；候选版本待核对");
  var remote=await Git(repo,token,"config","--get-all","remote.origin.url");var urls=remote.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries);
  r.Repository=urls.Length==1?GithubTarget(urls[0].Trim()):"";
  if(r.Repository=="")r.Warnings.Add("origin 未配置为唯一受支持的 GitHub URL；不会尝试未知主机");
  var push=await Git(repo,token,"config","--get-all","remote.origin.pushurl");
  if(push.Code==0&&push.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).Any(x=>GithubTarget(x.Trim())!=r.Repository)){r.Repository="";r.Warnings.Add("pushurl 与 origin 不一致；目标绑定已阻断");}
  var rows=await Required("ls-files","--stage","-z");var files=new Dictionary<string,PublishFile>(StringComparer.Ordinal);
  foreach(var row in rows.Split('\0',StringSplitOptions.RemoveEmptyEntries)){
   int tab=row.IndexOf('\t');if(tab<0)throw new IOException("索引格式无效");var meta=row[..tab].Split(' ');var path=row[(tab+1)..];SafeFile(repo,path);
   if(meta.Length!=3||meta[0]=="120000"||meta[0]=="160000"||meta[2]!="0")throw new IOException("链接/子模块/冲突索引不支持，未标成可发布");
   files.Add(path,new PublishFile{Path=path,IndexBlob=meta[1]});
  }
  var status=await Required("status","--porcelain=v1","-z","--untracked-files=all","--ignore-submodules=none");
  var entries=status.Split('\0',StringSplitOptions.RemoveEmptyEntries);
  for(int i=0;i<entries.Length;i++){
   var row=entries[i];if(row.Length<4||row[2]!=' ')throw new IOException("状态格式无效");string path=row[3..];SafeFile(repo,path);
   if(!files.TryGetValue(path,out var f)){f=new PublishFile{Path=path};files.Add(path,f);}f.Status=row[..2];
   if(f.Status.Contains('R')||f.Status.Contains('C')){if(++i>=entries.Length)throw new IOException("重命名源缺失");var old=entries[i];SafeFile(repo,old);f.Risks.Add("重命名/复制须连同原路径核对："+old);}
  }
  if(files.Count>1000)throw new IOException("文件数超过本阶段 1000 项上限");r.Worktree=entries.Length==0?"committed_clean":"uncommitted";
  long total=0;
  foreach(var f in files.Values.OrderBy(x=>x.Path,StringComparer.Ordinal)){
   token.ThrowIfCancellationRequested();var full=SafeFile(repo,f.Path);f.Bytes=File.Exists(full)?new FileInfo(full).Length:0;PathRisk(f);
   if(f.Bytes>5*1024*1024){f.WorkingSha256="not_read_large";}else if(File.Exists(full)){
    total+=f.Bytes;if(total>25*1024*1024)throw new IOException("内容审查超过 25 MiB 本阶段上限");var bytes=File.ReadAllBytes(full);f.WorkingSha256=Hash(bytes);Scan(f,Encoding.UTF8.GetString(bytes));
   }else f.WorkingSha256="missing";
   if(f.IndexBlob!=""){
    var size=await Required("cat-file","-s",f.IndexBlob);if(!long.TryParse(size.Trim(),out var n))throw new IOException("索引大小未知");
    if(n>5*1024*1024)f.Risks.Add("索引 blob 超过 5 MiB 阈值，未读取");
    else{var blob=await Required("cat-file","blob",f.IndexBlob);Scan(f,blob);}
   }
   if(f.Status!="clean"&&f.Bytes<=5*1024*1024){
    string spec=":(literal)"+f.Path;
    var staged=await Required("diff","--cached","--no-ext-diff","--no-textconv","--unified=2","--",spec);
    var working=await Required("diff","--no-ext-diff","--no-textconv","--unified=2","--",spec);
    var diff="[index vs HEAD]\n"+staged+"\n[working vs index]\n"+working;
    if(f.Status=="??"&&File.Exists(full))diff+="\n[untracked content]\n"+File.ReadAllText(full);
    f.Diff=Redact(diff);if(f.Diff.Length>12000){f.Diff=f.Diff[..12000]+"\n[预览截断：须人工完整审查]";f.Risks.Add("diff 预览截断");}
   }
   r.Files.Add(f);
  }
  r.Stamp=HashText(JsonSerializer.Serialize(new{r.Subject,r.Branch,r.Commit,r.Worktree,r.Repository,r.MetadataHash,r.Files}));return r;
 }
 public async Task<PublishReport> ObserveGithub(PublishReport old,CancellationToken token){
  var r=await Inspect(old.Subject,token);r.Evidence=old.Evidence;CarryRemoteCache(old,r);if(r.Stamp!=old.Stamp)throw new IOException("源状态已改变，请先重新检查本地");
  if(r.Repository==""||r.Branch=="detached"||r.Commit=="unborn"){r.RemoteDetail="目标或分支不完整，远端未知";return r;}
  try{
   var repo=Repo(r.Subject);
   var user=await Command(GithubTool(),repo,new[]{"api","user","--jq",".login"},token);
   if(user.Code!=0||!Regex.IsMatch(user.Text.Trim(),@"^[A-Za-z0-9][A-Za-z0-9-]{0,38}$"))throw new IOException("账号未验证");
   r.Account=user.Text.Trim();
   var info=await Command(GithubTool(),repo,new[]{"api","repos/"+r.Repository,"--jq","{full_name: .full_name, private: .private, push: .permissions.push}"},token);
   if(info.Code!=0)throw new IOException("仓库信息不可用");
   using(var parsed=JsonDocument.Parse(info.Text)){
    var e=parsed.RootElement;if(!string.Equals(e.GetProperty("full_name").GetString(),r.Repository,StringComparison.OrdinalIgnoreCase))throw new IOException("仓库身份不一致");
    r.Visibility=e.GetProperty("private").GetBoolean()?"private":"public";
    if(!e.GetProperty("push").GetBoolean())throw new IOException("当前账号没有目标推送权限");
   }
   var remote=await Command(GithubTool(),repo,new[]{"api","repos/"+r.Repository+"/git/ref/heads/"+Uri.EscapeDataString(r.Branch),"--jq",".object.sha"},token);
   if(remote.Code!=0||!Regex.IsMatch(remote.Text.Trim(),"^[a-f0-9]{40,64}$"))throw new IOException("远端分支不可用或不存在");
   r.RemoteSha=remote.Text.Trim();r.RemoteObservedAt=DateTime.UtcNow.ToString("O");
   bool known=(await Git(repo,token,"cat-file","-e",r.RemoteSha+"^{commit}")).Code==0;
   int remoteAncestor=known?(await Git(repo,token,"merge-base","--is-ancestor",r.RemoteSha,r.Commit)).Code:128;
   int localAncestor=known?(await Git(repo,token,"merge-base","--is-ancestor",r.Commit,r.RemoteSha)).Code:128;
   bool complete=known&&remoteAncestor<=1&&localAncestor<=1&&!File.Exists(SafeFile(repo,".git/shallow"));
   r.RemoteState=RemoteRelation(r.Commit,r.RemoteSha,complete,remoteAncestor==0,localAncestor==0);
   r.RemoteDetail=r.RemoteState=="unknown"?"远端 SHA 已观测，但本地祖先信息缺失、不完整或检查失败；未 fetch，关系未知":"只读 GitHub API 与本地祖先检查；仅代表上述观测时刻";
   if(r.RemoteState=="committed_unpushed"){
    var commits=await Git(repo,token,"log","--format=%H",r.RemoteSha+".."+r.Commit);
    if(commits.Code!=0)throw new IOException("未推送提交范围不可用");r.UnpushedCommits=commits.Text.Split('\n',StringSplitOptions.RemoveEmptyEntries).ToList();
    var difference=await Git(repo,token,"diff","--no-ext-diff","--no-textconv","--unified=2",r.RemoteSha,r.Commit,"--");
    if(difference.Code!=0)throw new IOException("远端到候选差异不可用");r.CommittedDiff=Redact(difference.Text);
    if(r.CommittedDiff.Length>16000){r.CommittedDiff=r.CommittedDiff[..16000]+"\n[提交差异预览截断]";r.Warnings.Add("提交差异超过预览上限，完整历史须人工审查");}
    r.Warnings.Add("拟推送历史包含以上 commit；未对所有历史 blob 做秘密审计，必须人工核对");
   }
   var latest=await Inspect(r.Subject,token);if(latest.Stamp!=r.Stamp)throw new IOException("联网期间本地已改变");
  }catch(OperationCanceledException){throw;}catch{
   r.RemoteState="unknown";r.RemoteSha="unknown";r.RemoteObservedAt="";r.RemoteDetail="账号、网络、目标权限或分支检查失败；未当作同步，未修改认证";
  }
  return r;
 }
 public void SaveReport(PublishReport r){if(r.RemoteObservedAt!=""){r.LastRemoteSha=r.RemoteSha;r.LastRemoteObservedAt=r.RemoteObservedAt;r.LastVerifiedAccount=r.Account;}File.WriteAllText(Store(r.Subject,"last-check.json"),JsonSerializer.Serialize(r,Workspace.Json));}
 internal static void CarryRemoteCache(PublishReport? old,PublishReport next){
  if(old==null||old.Subject!=next.Subject||old.Repository!=next.Repository||old.Branch!=next.Branch||old.MetadataHash!=next.MetadataHash)return;
  next.LastRemoteSha=old.RemoteObservedAt!=""?old.RemoteSha:old.LastRemoteSha;
  next.LastRemoteObservedAt=old.RemoteObservedAt!=""?old.RemoteObservedAt:old.LastRemoteObservedAt;
  next.LastVerifiedAccount=old.RemoteObservedAt!=""?old.Account:old.LastVerifiedAccount;
 }
 public PublishReport? LoadReport(string subject){
  var path=Store(subject,"last-check.json");if(!File.Exists(path))return null;
  try{if(new FileInfo(path).Length>4000000)return null;var r=JsonSerializer.Deserialize<PublishReport>(File.ReadAllText(path));return r?.Subject==subject?r:null;}catch(JsonException){return null;}
 }
 public PublishBinding? LoadBinding(string subject){
  var path=Store(subject,"binding.json");if(!File.Exists(path))return null;
  try{if(new FileInfo(path).Length>20000)return null;var b=JsonSerializer.Deserialize<PublishBinding>(File.ReadAllText(path));return b?.Subject==subject?b:null;}catch(JsonException){return null;}
 }
 public static bool BindingMatches(PublishReport r,PublishBinding? b)=>b!=null&&b.Subject==r.Subject&&b.Repository==r.Repository&&b.Branch==r.Branch&&b.Account==r.Account&&b.Visibility==r.Visibility&&b.MetadataHash==r.MetadataHash;
 public PublishBinding Bind(PublishReport r){
  if(r.RemoteObservedAt==""||r.Repository==""||r.Account=="unverified"||!DateTimeOffset.TryParse(r.RemoteObservedAt,out var observed)||DateTimeOffset.UtcNow-observed>TimeSpan.FromMinutes(5))throw new IOException("请先联网核对真实账号、仓库和分支，五分钟内确认目标");
  var b=new PublishBinding{Subject=r.Subject,Repository=r.Repository,Branch=r.Branch,Account=r.Account,Visibility=r.Visibility,MetadataHash=r.MetadataHash,ConfirmedAt=DateTime.UtcNow.ToString("O")};
  File.WriteAllText(Store(r.Subject,"binding.json"),JsonSerializer.Serialize(b,Workspace.Json));return b;
 }
 public void RecordOperation(string subject,string state){File.WriteAllText(Store(subject,"operation.json"),JsonSerializer.Serialize(new{subject,state,at=DateTime.UtcNow.ToString("O")},Workspace.Json));}
 public async Task<string> Export(PublishReport r,IEnumerable<string> chosen,CancellationToken token){
  var latest=await Inspect(r.Subject,token);if(latest.Stamp!=r.Stamp)throw new IOException("本地状态或文件内容已改变；旧预览已过期，重新检查后再导出");
  var paths=chosen.Distinct(StringComparer.Ordinal).OrderBy(x=>x,StringComparer.Ordinal).ToArray();
  if(paths.Length==0)throw new IOException("至少选择一个精确文件路径");
  var selected=r.Files.Where(f=>paths.Contains(f.Path,StringComparer.Ordinal)).ToList();if(selected.Count!=paths.Length)throw new IOException("选择不属于当前检查范围");
  var binding=LoadBinding(r.Subject);
  foreach(var evidence in r.Evidence){var actual=Evidence(r,evidence.Kind,evidence.Path);if(actual.Sha256!=evidence.Sha256||evidence.SourceFingerprint!=r.Stamp)throw new IOException("登记证据已改变或不属于当前候选");}
  var plan=new PublishPlan{ExportedAt=DateTime.UtcNow.ToString("O"),Report=JsonSerializer.Deserialize<PublishReport>(JsonSerializer.Serialize(r))!,Binding=binding,SelectedFiles=selected,CandidateCommit=r.Commit,SourceFingerprint=r.Stamp};
  plan.Report.Files=selected;plan.Report.CommittedDiff="";
  plan.Report.Warnings.Add("所选文件仅是审查范围；Git push会传播整个可达提交历史，不能只推送这些文件。本计划不可执行。");
  if(r.RemoteState=="committed_unpushed"){
   var diff=await Git(Repo(r.Subject),token,new[]{"diff","--no-ext-diff","--no-textconv","--unified=2",r.RemoteSha,r.Commit,"--"}.Concat(paths.Select(x=>":(literal)"+x)).ToArray());
   if(diff.Code!=0)throw new IOException("选定提交差异不可用");plan.Report.CommittedDiff=Redact(diff.Text);
   if(plan.Report.CommittedDiff.Length>16000){plan.Report.CommittedDiff=plan.Report.CommittedDiff[..16000]+"\n[预览截断]";plan.Report.Warnings.Add("选定提交差异预览截断，需人工完整审查");}
  }
  plan.TestEvidenceSha256=r.Evidence.FirstOrDefault(x=>x.Kind=="test")?.Sha256??"not_registered";
  plan.PackageSha256=r.Evidence.FirstOrDefault(x=>x.Kind=="package")?.Sha256??"not_registered";
  if(r.UnpushedCommits.Count>0||selected.Any(f=>f.Risks.Count>0)||r.Repository==""||!BindingMatches(r,binding)||r.Branch=="main"||r.RemoteState=="unknown"||r.Worktree!="committed_clean")plan.State="blocked_or_review_required";
  if(!DateTimeOffset.TryParse(r.RemoteObservedAt,out var observed)||DateTimeOffset.UtcNow-observed>TimeSpan.FromMinutes(5)){plan.State="blocked_or_review_required";plan.Report.Warnings=new(r.Warnings){"远端观测已缺失或过期；当前远端未知，导出仅供审查"};}
  token.ThrowIfCancellationRequested();
  var dir=Store(r.Subject,"plan-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..8]+".json");
  var json=JsonSerializer.Serialize(plan,Workspace.Json);using(var stream=new FileStream(dir,FileMode.CreateNew,FileAccess.Write,FileShare.None)){var bytes=Encoding.UTF8.GetBytes(json);stream.Write(bytes);stream.Flush(true);}
  return Path.GetRelativePath(w.Root,dir).Replace('\\','/');
 }
 public string GithubTool(){
  var path=w.Safe("runs/publication/gh-tool.json");
  if(File.Exists(path)){if(new FileInfo(path).Length>4096)throw new IOException("工具配置过大");return JsonSerializer.Deserialize<string>(File.ReadAllText(path))??"gh";}
  return Environment.GetEnvironmentVariable("WORKBENCH_GH_PATH")??"gh";
 }
 public void SetGithubTool(string path){
  path=path.Trim();if(path!="gh"&&(!Path.IsPathFullyQualified(path)||!string.Equals(Path.GetFileName(path),"gh.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(path)))throw new IOException("只登记已安装 gh.exe 的完整路径，或使用 PATH 中的 gh");
  Directory.CreateDirectory(w.Safe("runs/publication"));File.WriteAllText(w.Safe("runs/publication/gh-tool.json"),JsonSerializer.Serialize(path));
 }
 public PublishEvidence Evidence(PublishReport r,string kind,string relative){
  if(kind!="test"&&kind!="package")throw new IOException("证据类型不支持");
  relative=relative.Replace('\\','/');if(relative.Split('/').Any(x=>x==".."||x=="."||x==""))throw new IOException("证据相对路径无效");if(!relative.StartsWith("runs/",StringComparison.Ordinal)&&!relative.StartsWith("releases/",StringComparison.Ordinal))throw new IOException("证据只可位于本工作区 runs 或 releases");
  var path=w.Safe(relative);if(!File.Exists(path)||new FileInfo(path).Length>100*1024*1024)throw new IOException("证据缺失或超过 100 MiB 上限");
  using var stream=File.OpenRead(path);return new PublishEvidence{Kind=kind,Path=relative,Sha256=Convert.ToHexString(SHA256.HashData(stream)),AssociatedCommit=r.Commit,SourceFingerprint=r.Stamp};
 }

}
