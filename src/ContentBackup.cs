using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Security.Cryptography;

namespace Workbench;
public class AssetEntry {public string Path{get;set;}="";public long Bytes{get;set;}public string Sha256{get;set;}="";}
public class AssetExclusion {public string Path{get;set;}="";public string Reason{get;set;}="";}
public class ContentBackup {
 public string Schema{get;set;}="workbench-project-content-v1";
 public string ProjectId{get;set;}="";
 public bool Demo{get;set;}
 public int Version{get;set;}
 public string SourceStamp{get;set;}="";
 public string ContentStamp{get;set;}="";
 public List<AssetEntry> Files{get;set;}=new();
 public List<string> Directories{get;set;}=new();
 public List<AssetExclusion> Excluded{get;set;}=new();
 public List<string> ReferenceIssues{get;set;}=new();
 public Dictionary<string,GitFacts> GitAtBackup{get;set;}=new();
 public string Scope{get;set;}="项目源代码、Markdown事实/历史、输入、素材、数据、测试证据与发布内容；排除缓存、疑似凭据、链接、.git元数据和已有备份输出。不是字节级整目录镜像；不抓取外部引用。";
}
public class RecoveryPreview {
 public string ArchivePath{get;set;}="";
 public string ArchiveSha256{get;set;}="";
 public string Destination{get;set;}="";
 public ContentBackup Content{get;set;}=new();
}
public partial class Workspace {
 const int MaxBackupFiles=5000;
 const long MaxAssetBytes=128L*1024*1024,MaxBackupBytes=1024L*1024*1024;
 public static string NormalizeArchiveName(string raw){
  if(raw==""||raw!=raw.Trim()||raw.Contains('\\')||Path.IsPathRooted(raw)||raw!=raw.Normalize(NormalizationForm.FormC))throw new InvalidDataException("备份路径不是规范相对路径");
  foreach(var segment in raw.Split('/')){
   if(segment==""||segment=="."||segment==".."||segment.EndsWith(".")||segment.EndsWith(" ")||segment.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(segment,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])([.]|$)",RegexOptions.IgnoreCase))throw new InvalidDataException("备份路径含非法Windows别名");
  }
  return raw;
 }
 public static string ExcludedAssetPath(string relative){
  var parts=relative.Split('/');var name=parts.Last();
  if(parts.Any(p=>new[]{".git",".aws",".ssh",".secrets",".credentials",".codex"}.Contains(p,StringComparer.OrdinalIgnoreCase)))return "Git/凭据/代理私有元数据";
  if(parts.Any(p=>new[]{"node_modules",".venv","venv",".cache","__pycache__","bin","obj",".vs"}.Contains(p,StringComparer.OrdinalIgnoreCase)))return "可重建依赖/缓存";
  if(relative.StartsWith("runs/exports",StringComparison.OrdinalIgnoreCase)||relative.StartsWith("runs/temp",StringComparison.OrdinalIgnoreCase))return "既有导出/临时输出";
  if(name.Equals("write.lock",StringComparison.OrdinalIgnoreCase)||name.Contains(".pending.",StringComparison.OrdinalIgnoreCase))return "写锁/未提交临时文件";
  if(name.StartsWith(".env",StringComparison.OrdinalIgnoreCase)||Regex.IsMatch(name,@"(?i)^(credentials|secrets|tokens|id_rsa|id_ed25519|\.git-credentials|\.npmrc)([._-]|$)")||new[]{".pem",".key",".pfx",".p12",".jks",".keystore",".user"}.Contains(Path.GetExtension(name),StringComparer.OrdinalIgnoreCase))return "疑似凭据/私钥文件";
  return "";
 }
 static bool SuspectedSensitive(byte[] bytes,string path){
  if(bytes.Length>2*1024*1024||!new[]{".txt",".md",".json",".yaml",".yml",".xml",".config",".properties",".ini",".ps1",".js",".ts",".cs",".py",".html"}.Contains(Path.GetExtension(path),StringComparer.OrdinalIgnoreCase))return false;
  string text=Encoding.UTF8.GetString(bytes);
  return Regex.IsMatch(text,@"-----BEGIN [A-Z ]*PRIVATE KEY-----|sk-[A-Za-z0-9_-]{20,}|(?i)(api[_-]?key|password|access[_-]?token|client[_-]?secret)[""']?\s*[:=]\s*[""']?[A-Za-z0-9+/=_-]{12,}");
 }
 public ContentBackup PreviewFullBackup(ProjectRecord p){
  RequireFresh(p);var result=new ContentBackup{ProjectId=p.Id,Demo=p.Demo,Version=p.Version,SourceStamp=p.SourceStamp};
  string projectRoot=ProjectPath(p.Id,"README.md");projectRoot=Path.GetDirectoryName(projectRoot)!;
  var pending=new Stack<(string Full,string Relative)>();pending.Push((projectRoot,""));
  long total=0;
  while(pending.Count>0){
   var current=pending.Pop();
   if((File.GetAttributes(current.Full)&FileAttributes.ReparsePoint)!=0)throw new IOException("拒绝扫描链接目录");
   foreach(var child in Directory.EnumerateFileSystemEntries(current.Full).OrderBy(x=>x,StringComparer.Ordinal)){
    string relative=current.Relative==""?Path.GetFileName(child):current.Relative+"/"+Path.GetFileName(child);
    NormalizeArchiveName(relative);var reason=ExcludedAssetPath(relative);var attributes=File.GetAttributes(child);
    if(reason!=""){result.Excluded.Add(new(){Path=relative,Reason=reason});continue;}
    if((attributes&FileAttributes.ReparsePoint)!=0){result.Excluded.Add(new(){Path=relative,Reason="链接排除，未跟随"});continue;}
    ProjectPath(p.Id,relative);
    if(Directory.Exists(child)){if(result.Directories.Count>=5000)throw new IOException("目录超过上限");result.Directories.Add(relative);pending.Push((child,relative));continue;}
    var info=new FileInfo(child);if(info.Length>MaxAssetBytes)throw new IOException("单文件超过128MiB，本阶段停止，不部分声称完成");
    total+=info.Length;if(total>MaxBackupBytes||result.Files.Count>=MaxBackupFiles)throw new IOException("项目内容超过本阶段1GiB/5000文件上限");
    byte[] data=File.ReadAllBytes(child);
    if(SuspectedSensitive(data,relative)){result.Excluded.Add(new(){Path=relative,Reason="疑似敏感内容；未写入备份/日志"});continue;}
    result.Files.Add(new(){Path=relative,Bytes=data.LongLength,Sha256=Hash(data)});
   }
  }
  result.Files=result.Files.OrderBy(f=>f.Path,StringComparer.Ordinal).ToList();result.Directories.Sort(StringComparer.Ordinal);result.Excluded=result.Excluded.OrderBy(x=>x.Path,StringComparer.Ordinal).ToList();
  foreach(var relative in p.Tasks.SelectMany(t=>t.RelatedFiles.Concat(t.Evidence==""?Array.Empty<string>():new[]{t.Evidence})).Distinct()){
   try{NormalizeArchiveName(relative);if(!result.Files.Any(f=>string.Equals(f.Path,relative,StringComparison.OrdinalIgnoreCase)))result.ReferenceIssues.Add(relative+"：缺失或被安全策略排除，未抓取外部引用");}
   catch{result.ReferenceIssues.Add("登记引用无效，未读取");}
  }
  foreach(var knowledge in p.Knowledge){
   string source=knowledge.Source.Trim().Replace('\\','/');
   if(Path.IsPathRooted(source)){result.ReferenceIssues.Add(knowledge.Id+"：绝对引用保留文本，未访问/改写；恢复副本需本人核对");continue;}
   if(new[]{"repo/","docs/","assets/","data/","runs/","releases/","archive/"}.Any(prefix=>source.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))){
    try{NormalizeArchiveName(source);if(!result.Files.Any(f=>f.Path.Equals(source,StringComparison.OrdinalIgnoreCase)))result.ReferenceIssues.Add(knowledge.Id+"："+source+"：登记知识文件引用缺失/被排除");}
    catch{result.ReferenceIssues.Add(knowledge.Id+"：知识文件引用无效，未读取");}
   }
  }
  var required=p.Format=="markdown-v3"?new[]{"repo/docs/CURRENT.md","repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}.Concat(MarkdownCodec.Strings(Pointer(p.Id,PointerPath(p.Id)),"files").Select(f=>"repo/docs/snapshots/"+MarkdownCodec.String(Pointer(p.Id,PointerPath(p.Id)),"snapshot")+"/"+f)):new[]{"repo/docs/records.json"};
  foreach(var file in required)if(!result.Files.Any(f=>f.Path==file))result.ReferenceIssues.Add("现行事实文档不在安全清单："+file+"；不能创建完整内容备份");
  foreach(var task in p.Tasks.OrderBy(t=>t.Id,StringComparer.Ordinal))result.GitAtBackup[task.Id]=ReadGit(p,task);
  result.ContentStamp=ContentFingerprint(result);RequireFresh(p);return result;
 }
 internal static string ContentFingerprint(ContentBackup p)=>Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new{p.ProjectId,p.Demo,p.Version,p.SourceStamp,p.Files,p.Directories,p.Excluded,p.ReferenceIssues,p.GitAtBackup})));
 public string CreateFullBackup(ProjectRecord p,ContentBackup preview){
  using var writeLock=new FileStream(ProjectPath(p.Id,"repo/docs/write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var current=PreviewFullBackup(Load(p.Id));
  if(current.ReferenceIssues.Any(x=>x.StartsWith("现行事实")))throw new InvalidOperationException("现行事实源缺失/被安全策略排除；保留原件，不能生成无法恢复的完整备份");
  if(current.ContentStamp!=preview.ContentStamp||preview.ProjectId!=p.Id)throw new InvalidOperationException("项目内容已变化，保留预览，请重新核对");
  var folder=Safe("runs/exports");Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,"project-content-"+p.Id+"-"+Guid.NewGuid().ToString("N")+".zip");var pendingPath=path+".pending";
  try{
   using(var zip=ZipFile.Open(pendingPath,ZipArchiveMode.Create)){
    foreach(var f in current.Files){
     var entry=zip.CreateEntry("project/"+f.Path,CompressionLevel.Optimal);
     using var input=new FileStream(ProjectPath(p.Id,f.Path),FileMode.Open,FileAccess.Read,FileShare.Read);using var output=entry.Open();input.CopyTo(output);
    }
    var manifest=zip.CreateEntry("workbench-backup.json");using var writer=new StreamWriter(manifest.Open(),new UTF8Encoding(false));writer.Write(JsonSerializer.Serialize(current,Json));
   }
   var after=PreviewFullBackup(Load(p.Id));if(after.ContentStamp!=current.ContentStamp)throw new InvalidOperationException("备份期间来源变化，候选ZIP保留，未宣布成功");
   var checkedArchive=ReadContentArchive(pendingPath);
   if(checkedArchive.Content.ContentStamp!=current.ContentStamp)throw new IOException("备份候选校验不一致");
   File.Move(pendingPath,path);return path;
  }catch{throw new IOException("完整内容备份未完成；原项目不变，若产生候选ZIP则保留："+pendingPath);}
 }
 string RootLocalFile(string path){
  var relative=Path.GetRelativePath(Root,Path.GetFullPath(path));var safe=Safe(relative);
  if(!File.Exists(safe))throw new FileNotFoundException("本项目内备份文件不存在");
  return safe;
 }
 RecoveryPreview ReadContentArchive(string path){
  path=RootLocalFile(path);
  if(new FileInfo(path).Length>MaxBackupBytes)throw new InvalidDataException("压缩文件超过上限");
  using var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
  var result=new RecoveryPreview{ArchivePath=path,ArchiveSha256=Convert.ToHexString(SHA256.HashData(stream))};stream.Position=0;using var zip=new ZipArchive(stream,ZipArchiveMode.Read);
  if(zip.Entries.Count>MaxBackupFiles+1)throw new InvalidDataException("ZIP条目过多");
  var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
  foreach(var entry in zip.Entries){
   NormalizeArchiveName(entry.FullName);
   if(!names.Add(entry.FullName))throw new InvalidDataException("ZIP含重复/大小写冲突路径");
   if(entry.Length>MaxAssetBytes||entry.Length<0||(total+=entry.Length)>MaxBackupBytes)throw new InvalidDataException("ZIP展开大小超过上限");
   int unixMode=(entry.ExternalAttributes>>16)&0xF000;if(unixMode==0xA000)throw new InvalidDataException("ZIP含符号链接");
   if(entry.FullName!="workbench-backup.json"&&!entry.FullName.StartsWith("project/",StringComparison.Ordinal))throw new InvalidDataException("ZIP含未登记顶层内容");
  }
  var manifest=zip.GetEntry("workbench-backup.json")??throw new InvalidDataException("缺备份清单");
  if(manifest.Length>2*1024*1024)throw new InvalidDataException("清单过大");
  using(var reader=new StreamReader(manifest.Open(),Encoding.UTF8))result.Content=JsonSerializer.Deserialize<ContentBackup>(reader.ReadToEnd())??throw new InvalidDataException("清单无效");
  var m=result.Content;
  if(m.Schema!="workbench-project-content-v1"||!Regex.IsMatch(m.ProjectId,"^P-[a-f0-9]{12}$")||m.Files==null||m.Directories==null||m.Excluded==null||m.ReferenceIssues==null||m.GitAtBackup==null||m.Version<1||!Regex.IsMatch(m.SourceStamp,"^[A-F0-9]{64}$"))throw new InvalidDataException("备份身份/模式/版本无效");
  if(m.Files.Count!=zip.Entries.Count-1||m.Files.Count>MaxBackupFiles||m.Files.Select(f=>f.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count()!=m.Files.Count)throw new InvalidDataException("文件清单不一致");
  if(m.Directories.Count>5000||m.Directories.Distinct(StringComparer.OrdinalIgnoreCase).Count()!=m.Directories.Count)throw new InvalidDataException("目录重复");
  foreach(var directory in m.Directories){NormalizeArchiveName(directory);if(ExcludedAssetPath(directory)!="")throw new InvalidDataException("清单含被排除目录");}
  foreach(var f in m.Files){
   NormalizeArchiveName(f.Path);if(ExcludedAssetPath(f.Path)!=""||!Regex.IsMatch(f.Sha256,"^[A-F0-9]{64}$"))throw new InvalidDataException("清单含不安全文件");
   var entry=zip.GetEntry("project/"+f.Path)??throw new InvalidDataException("清单文件缺失");
   if(entry.Length!=f.Bytes)throw new InvalidDataException("文件大小不一致");
   using var input=entry.Open();using var hash=IncrementalHash.CreateHash(HashAlgorithmName.SHA256);using var inspect=new MemoryStream();
   byte[] buffer=new byte[65536];long readTotal=0;int count;
   while((count=input.Read(buffer))>0){readTotal+=count;if(readTotal>f.Bytes||readTotal>MaxAssetBytes)throw new InvalidDataException("ZIP实际展开大小超出清单");hash.AppendData(buffer,0,count);if(f.Bytes<=2*1024*1024)inspect.Write(buffer,0,count);}
   if(readTotal!=f.Bytes||Convert.ToHexString(hash.GetHashAndReset())!=f.Sha256)throw new InvalidDataException("文件SHA256/实际大小不一致");
   if(f.Bytes<=2*1024*1024&&SuspectedSensitive(inspect.ToArray(),f.Path))throw new InvalidDataException("ZIP内容被凭据策略拒绝");
   if(m.Directories.Any(d=>string.Equals(d,f.Path,StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("文件/目录冲突");
  }
  var allPaths=m.Files.Select(f=>f.Path).ToArray();
  foreach(var file in allPaths)if(allPaths.Any(other=>other.StartsWith(file+"/",StringComparison.OrdinalIgnoreCase))||m.Directories.Any(d=>d.StartsWith(file+"/",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("ZIP文件/父目录冲突");
  if(ContentFingerprint(m)!=m.ContentStamp)throw new InvalidDataException("内容清单指纹无效");
  return result;
 }
 public RecoveryPreview PreviewContentRestore(string path){
  var result=ReadContentArchive(path);result.Destination=Safe("runs/restored/restore-"+Guid.NewGuid().ToString("N"));return result;
 }
 public string RestoreContentCopy(RecoveryPreview preview,bool explicitlyConfirmed){
  if(!explicitlyConfirmed)throw new InvalidOperationException("恢复需要本人确认预览");
  string expectedParent=Safe("runs/restored")+Path.DirectorySeparatorChar;
  var destination=Safe(Path.GetRelativePath(Root,preview.Destination));
  if(!destination.StartsWith(expectedParent,StringComparison.OrdinalIgnoreCase)||Path.GetDirectoryName(destination)!=expectedParent.TrimEnd(Path.DirectorySeparatorChar)||!Regex.IsMatch(Path.GetFileName(destination),"^restore-[a-f0-9]{32}$"))throw new InvalidOperationException("恢复目标不是本项目独立恢复副本");
  if(Directory.Exists(destination)||File.Exists(destination))throw new IOException("恢复目标冲突；拒绝覆盖，原件保留");
  var fresh=ReadContentArchive(preview.ArchivePath);
  if(fresh.ArchiveSha256!=preview.ArchiveSha256||fresh.Content.ContentStamp!=preview.Content.ContentStamp)throw new InvalidOperationException("ZIP在预览后变化，请重新预览");
  var staging=Safe("runs/restore-staging/stage-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(staging);string source=RootLocalFile(preview.ArchivePath);
  try{
   File.Copy(Path.Combine(Root,"project.json"),Path.Combine(staging,"project.json"));
   var restored=new Workspace(staging,fresh.Content.Demo);
   string relativeRoot="data/"+restored.Mode+"/Projects/"+fresh.Content.ProjectId;
   Directory.CreateDirectory(restored.Safe(relativeRoot));
   foreach(var directory in fresh.Content.Directories)Directory.CreateDirectory(restored.Safe(relativeRoot+"/"+directory));
   using(var archive=new ZipArchive(new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read),ZipArchiveMode.Read)){
    foreach(var f in fresh.Content.Files){
     var target=restored.Safe(relativeRoot+"/"+f.Path);Directory.CreateDirectory(Path.GetDirectoryName(target)!);
     using var input=archive.GetEntry("project/"+f.Path)!.Open();using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
     if(Hash(File.ReadAllBytes(target))!=f.Sha256)throw new IOException("恢复文件校验失败");
    }
   }
   var record=restored.Load(fresh.Content.ProjectId);
   if(record.SourceStamp!=fresh.Content.SourceStamp||record.Version!=fresh.Content.Version)throw new InvalidDataException("恢复事实源与备份版本不一致");
   if(Hash(File.ReadAllBytes(source))!=preview.ArchiveSha256)throw new IOException("恢复期间ZIP改变");
   File.WriteAllText(Path.Combine(staging,"recovery.json"),JsonSerializer.Serialize(new RecoveryMarker{ProjectId=record.Id,Demo=fresh.Content.Demo,OriginSourceStamp=record.SourceStamp,OriginArchiveSha256=preview.ArchiveSha256},Json),new UTF8Encoding(false));
   Directory.CreateDirectory(expectedParent.TrimEnd(Path.DirectorySeparatorChar));Directory.Move(staging,destination);
   return destination;
  }catch{throw new IOException("恢复未完成；原项目与ZIP保留，失败候选保留于："+staging);}
 }
 public string DescribeContent(ContentBackup plan)=>plan.Files.Count+" 个文件，"+plan.Files.Sum(f=>f.Bytes)+" 字节；版本 "+plan.Version+"\n"+plan.Scope+"\n排除 "+plan.Excluded.Count+" 项；引用问题 "+plan.ReferenceIssues.Count+" 项。\n内容指纹："+plan.ContentStamp;
}
