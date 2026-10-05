using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Encodings.Web;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Workbench;
public static class MarkdownCodec {
 static readonly JsonSerializerOptions Readable=new(){Encoder=JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
 public static string Document(string title,Dictionary<string,object?> metadata,Dictionary<string,string> bodies) {
  var text=new StringBuilder("---\n");
  foreach(var pair in metadata)text.Append(pair.Key).Append(": ").Append(JsonSerializer.Serialize(pair.Value,Readable)).Append('\n');
  text.Append("---\n\n# ").Append(title).Append("\n\n");
  foreach(var pair in bodies){
   if(pair.Value.Contains("<!-- wb:")||pair.Value.Contains("<!-- /wb:"))throw new InvalidDataException("正文不能包含保留的文档字段标记。");
   text.Append("## ").Append(pair.Key).Append("\n<!-- wb:").Append(pair.Key).Append(" -->\n").Append(pair.Value.Trim()).Append("\n<!-- /wb:").Append(pair.Key).Append(" -->\n\n");
  }
  return text.ToString();
 }
 public static Dictionary<string,JsonElement> Metadata(string text) {
  text=text.Replace("\r\n","\n");
  if(!text.StartsWith("---\n"))throw new InvalidDataException("缺少Markdown元数据入口。");
  int end=text.IndexOf("\n---\n",4,StringComparison.Ordinal);if(end<0)throw new InvalidDataException("元数据结束标记缺失。");
  var data=new Dictionary<string,JsonElement>();
  foreach(var line in text[4..end].Split('\n')){
   int colon=line.IndexOf(':');if(colon<1)throw new InvalidDataException("无效元数据行。");
   string key=line[..colon];if(data.ContainsKey(key))throw new InvalidDataException("重复元数据字段："+key);
   using var doc=JsonDocument.Parse(line[(colon+1)..]);data.Add(key,doc.RootElement.Clone());
  }
  return data;
 }
 public static string Body(string text,string key) {
  var match=Regex.Matches(text.Replace("\r\n","\n"),"<!-- wb:"+Regex.Escape(key)+" -->\n(.*?)\n<!-- /wb:"+Regex.Escape(key)+" -->",RegexOptions.Singleline);
  if(match.Count!=1)throw new InvalidDataException("正文标记缺失/重复："+key);
  return match[0].Groups[1].Value.Trim();
 }
 public static string String(Dictionary<string,JsonElement> m,string key)=>m.TryGetValue(key,out var value)?value.GetString()??"":throw new InvalidDataException("缺少字段："+key);
 public static string[] Strings(Dictionary<string,JsonElement> m,string key)=>m[key].Deserialize<string[]>()??Array.Empty<string>();
}
public partial class Workspace {
 string LegacyPath(string id)=>ProjectPath(id,"repo/docs/records.json");
 string PointerPath(string id)=>ProjectPath(id,"repo/docs/CURRENT.md");
 string PreviousPath(string id)=>ProjectPath(id,"repo/docs/CURRENT.previous.md");
 static string ReadUtf8(string path)=>File.ReadAllText(path,Encoding.UTF8);
 Dictionary<string,JsonElement> Pointer(string id,string path,string? captured=null) {
  var m=MarkdownCodec.Metadata(captured??ReadUtf8(path));
  if(MarkdownCodec.String(m,"project_id")!=id||m["demo"].GetBoolean()!=Demo)throw new InvalidDataException("文档入口项目/模式不匹配。");
  var snapshot=MarkdownCodec.String(m,"snapshot");
  if(!Regex.IsMatch(snapshot,"^S-[0-9]+-[a-f0-9]{12}$"))throw new InvalidDataException("快照名非法。");
  foreach(var file in MarkdownCodec.Strings(m,"files"))ValidateSnapshotFile(file);
  return m;
 }
 static void ValidateSnapshotFile(string file){
  if(!Regex.IsMatch(file,"^(PROJECT|STATE)\\.md$|^tasks/T-[a-f0-9]{8}\\.md$|^knowledge/K-[A-Za-z0-9-]{1,40}\\.md$"))
   throw new InvalidDataException("非法快照文件名："+file);
 }
 string SnapshotPath(string id,string snapshot,string file){ValidateSnapshotFile(file);return ProjectPath(id,"repo/docs/snapshots/"+snapshot+"/"+file);}
 public string MarkdownEntry(string id)=>PointerPath(id);
 public string SnapshotFile(string id,string file) {
  var m=Pointer(id,PointerPath(id));
  if(!MarkdownCodec.Strings(m,"files").Contains(file))throw new InvalidOperationException("未登记文档。");
  return SnapshotPath(id,MarkdownCodec.String(m,"snapshot"),file);
 }
 ProjectRecord LoadFacts(string id) {
  if(!File.Exists(PointerPath(id))){
   if(File.Exists(ProjectPath(id,"repo/docs/format-v3.marker")))throw new InvalidDataException("Markdown事实入口缺失，不退回旧JSON。");
   var legacy=JsonSerializer.Deserialize<ProjectRecord>(ReadUtf8(LegacyPath(id)))??throw new InvalidDataException("空记录。");
   Validate(legacy);if(legacy.Id!=id||legacy.Demo!=Demo)throw new InvalidDataException("项目身份不匹配。");
   legacy.Format="legacy-json";legacy.SourceStamp=Hash(File.ReadAllBytes(LegacyPath(id)));return legacy;
  }
  return ReadSnapshot(id,PointerPath(id));
 }
 ProjectRecord ReadSnapshot(string id,string pointerPath) {
  var pointerText=ReadUtf8(pointerPath);var m=Pointer(id,pointerPath,pointerText);var files=MarkdownCodec.Strings(m,"files");var snapshot=MarkdownCodec.String(m,"snapshot");
  if(files.Distinct().Count()!=files.Length||!files.Contains("PROJECT.md")||!files.Contains("STATE.md"))throw new InvalidDataException("快照文件清单无效。");
  string initialStamp=ComputeStamp(id,pointerPath,m,pointerText);
  string projectText=ReadUtf8(SnapshotPath(id,snapshot,"PROJECT.md"));var pm=MarkdownCodec.Metadata(projectText);
  var p=new ProjectRecord{Id=MarkdownCodec.String(pm,"project_id"),Name=MarkdownCodec.Body(projectText,"项目名称"),Goal=MarkdownCodec.Body(projectText,"已批准项目目标"),Demo=pm["demo"].GetBoolean(),Version=pm["record_version"].GetInt32(),Format="markdown-v3"};
  if(p.Id!=id||p.Demo!=Demo||p.Version!=m["record_version"].GetInt32())throw new InvalidDataException("快照身份/版本不一致。");
  foreach(var file in files.Where(f=>f.StartsWith("tasks/"))) {
   var text=ReadUtf8(SnapshotPath(id,snapshot,file));var tm=MarkdownCodec.Metadata(text);EnsureIdentity(tm,id,p.Version);
   var t=new WorkTask {Id=MarkdownCodec.String(tm,"task_id"),Owner=MarkdownCodec.String(tm,"owner"),Status=MarkdownCodec.String(tm,"state"),Scope=MarkdownCodec.String(tm,"edit_scope"),Evidence=MarkdownCodec.String(tm,"evidence"),EvidenceHash=MarkdownCodec.String(tm,"evidence_sha256"),VerifiedAnchor=MarkdownCodec.String(tm,"verified_anchor"),VerifiedAt=MarkdownCodec.String(tm,"verified_at"),RelatedFiles=MarkdownCodec.Strings(tm,"related_files").ToList(),WorktreeAndBranch=MarkdownCodec.String(tm,"worktree_and_branch"),LastVerifiedCodeCommit=MarkdownCodec.String(tm,"last_verified_code_commit"),Goal=MarkdownCodec.Body(text,"任务目标"),Done=MarkdownCodec.Body(text,"已做与证据"),NotDone=MarkdownCodec.Body(text,"未做与阻碍"),Next=MarkdownCodec.Body(text,"下一步")};
   if(file!="tasks/"+t.Id+".md")throw new InvalidDataException("任务文件身份不一致。");p.Tasks.Add(t);
  }
  foreach(var file in files.Where(f=>f.StartsWith("knowledge/"))) {
   var text=ReadUtf8(SnapshotPath(id,snapshot,file));var km=MarkdownCodec.Metadata(text);EnsureIdentity(km,id,p.Version);
   var k=new Fact{Id=MarkdownCodec.String(km,"knowledge_id"),Role=MarkdownCodec.String(km,"source_role"),Source=MarkdownCodec.String(km,"source_reference"),Scope=MarkdownCodec.String(km,"module_scope"),Lifecycle=MarkdownCodec.String(km,"lifecycle"),Verification=MarkdownCodec.String(km,"verification"),Revision=MarkdownCodec.String(km,"approved_or_source_revision"),Supersedes=MarkdownCodec.String(km,"supersedes"),ConflictWith=MarkdownCodec.String(km,"conflict_with"),VerifiedAt=MarkdownCodec.String(km,"verified_at"),Text=MarkdownCodec.Body(text,"知识正文")};
   if(file!="knowledge/"+k.Id+".md")throw new InvalidDataException("知识文件身份不一致。");p.Knowledge.Add(k);
  }
  var sm=MarkdownCodec.Metadata(ReadUtf8(SnapshotPath(id,snapshot,"STATE.md")));EnsureIdentity(sm,id,p.Version);p.Events=MarkdownCodec.Strings(sm,"events").ToList();
  Validate(p);
  var expected=SnapshotDocuments(p);
  foreach(var file in files)CheckDocumentShape(file,ReadUtf8(SnapshotPath(id,snapshot,file)),expected[file]);
  p.ExternalChanges=new List<string>();
  if(m.TryGetValue("hashes",out var hashes))foreach(var file in files){if(!hashes.TryGetProperty(file,out var known)||known.GetString()!=Hash(File.ReadAllBytes(SnapshotPath(id,snapshot,file))))p.ExternalChanges.Add(file);}
  else throw new InvalidDataException("缺少快照基线指纹。");
  if(m.TryGetValue("template_hashes",out var th))foreach(var relative in new[]{"repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}){
   var path=ProjectPath(id,relative);if(!th.TryGetProperty(relative,out var known)||known.GetString()!=(File.Exists(path)?Hash(File.ReadAllBytes(path)):"MISSING"))p.ExternalChanges.Add(relative);
  }
  p.SourceStamp=ComputeStamp(id,pointerPath,m);
  if(initialStamp!=p.SourceStamp)throw new InvalidOperationException("读取期间文档发生变化，未载入混合版本，请重试刷新。");
  return p;
 }
 static void CheckDocumentShape(string file,string actual,string expected) {
  var a=MarkdownCodec.Metadata(actual);var b=MarkdownCodec.Metadata(expected);
  if(a.Count!=b.Count||a.Keys.Any(k=>!b.ContainsKey(k)||JsonSerializer.Serialize(a[k])!=JsonSerializer.Serialize(b[k])))throw new InvalidDataException(file+" 有未知或只读元数据改动，保留文件并人工核对。");
  string Shape(string s){
   s=s.Replace("\r\n","\n");int end=s.IndexOf("\n---\n",4,StringComparison.Ordinal);
   var body=s[(end+5)..];
   body=Regex.Replace(body,"(<!-- wb:[^>]+ -->)\n.*?\n(<!-- /wb:[^>]+ -->)","$1\n<editable-body>\n$2",RegexOptions.Singleline);
   return Regex.Replace(body,"[ \t]+$","",RegexOptions.Multiline).Trim();
  }
  if(Shape(actual)!=Shape(expected))throw new InvalidDataException(file+" 出现未登记段落，不会丢弃或覆盖，请放入规定字段/新增知识提案。");
  if(file=="STATE.md"&&MarkdownCodec.Body(actual,"事实入口")!=MarkdownCodec.Body(expected,"事实入口"))throw new InvalidDataException("STATE索引说明为只读字段，保留修改并核对。");
 }
 static void EnsureIdentity(Dictionary<string,JsonElement> m,string id,int version) {
  if(MarkdownCodec.String(m,"project_id")!=id||m["record_version"].GetInt32()!=version)throw new InvalidDataException("文档跨项目/版本冲突。");
 }
 string ComputeStamp(string id,string pointerPath,Dictionary<string,JsonElement> m,string? captured=null) {
  var text=new StringBuilder(captured??ReadUtf8(pointerPath));string snapshot=MarkdownCodec.String(m,"snapshot");
  foreach(var file in MarkdownCodec.Strings(m,"files").OrderBy(f=>f,StringComparer.Ordinal))text.Append(file).Append(Hash(File.ReadAllBytes(SnapshotPath(id,snapshot,file))));
  foreach(var relative in new[]{"repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}) {
   var path=ProjectPath(id,relative);text.Append(relative).Append(File.Exists(path)?Hash(File.ReadAllBytes(path)):"MISSING");
  }
  return Hash(Encoding.UTF8.GetBytes(text.ToString()));
 }
 Dictionary<string,string> SnapshotDocuments(ProjectRecord p) {
  var common=new Dictionary<string,object?>{{"project_id",p.Id},{"record_version",p.Version}};
  var docs=new Dictionary<string,string>();
  docs["PROJECT.md"]=MarkdownCodec.Document("项目目标与范围",new(common){{"demo",p.Demo},{"schema","workbench-markdown-v3"}},new(){{"项目名称",p.Name},{"已批准项目目标",p.Goal}});
  docs["STATE.md"]=MarkdownCodec.Document("当前状态与任务索引",new(common){{"events",p.Events},{"task_ids",p.Tasks.Select(t=>t.Id).ToArray()}},new(){{"事实入口","当前快照是唯一现行事实；任务及交接在 tasks，同一知识正文在 knowledge。缺少Git/真实测试时不得写成已核对。"}});
  foreach(var t in p.Tasks)docs["tasks/"+t.Id+".md"]=MarkdownCodec.Document("任务与增量交接",new(common){{"task_id",t.Id},{"owner",t.Owner},{"state",t.Status},{"edit_scope",t.Scope},{"related_files",t.RelatedFiles},{"evidence",t.Evidence},{"evidence_sha256",t.EvidenceHash},{"verified_anchor",t.VerifiedAnchor},{"verified_at",t.VerifiedAt},{"worktree_and_branch",t.WorktreeAndBranch},{"last_verified_code_commit",t.LastVerifiedCodeCommit},{"approval_pending","不授权其他项目、系统安装或删除"}},new(){{"任务目标",t.Goal},{"已做与证据",t.Done},{"未做与阻碍",t.NotDone},{"下一步",t.Next}});
  foreach(var k in p.Knowledge)docs["knowledge/"+k.Id+".md"]=MarkdownCodec.Document("项目知识",new(common){{"knowledge_id",k.Id},{"source_role",k.Role},{"source_reference",k.Source},{"module_scope",k.Scope},{"lifecycle",k.Lifecycle},{"verification",k.Verification},{"approved_or_source_revision",k.Revision},{"supersedes",k.Supersedes},{"conflict_with",k.ConflictWith},{"verified_at",k.VerifiedAt}},new(){{"知识正文",k.Text}});
  foreach(var file in docs.Keys)ValidateSnapshotFile(file);return docs;
 }
 static void WriteDurable(string path,string text) {
  Directory.CreateDirectory(Path.GetDirectoryName(path)!);
  using var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None);
  stream.Write(Encoding.UTF8.GetBytes(text));stream.Flush(true);
 }
 void SaveFacts(ProjectRecord p,int expected,string action,bool upgrade=false,bool acceptExternal=false) {
  Validate(p);foreach(var task in p.Tasks)foreach(var file in task.RelatedFiles)NormalizeRelated(file);using var writeLock=new FileStream(ProjectPath(p.Id,"repo/docs/write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  bool exists=File.Exists(PointerPath(p.Id))||File.Exists(LegacyPath(p.Id));
  var current=exists?LoadFacts(p.Id):null;
  if((current?.Version??0)!=expected)throw new InvalidOperationException("版本已改变，输入已保留。");
  if(current!=null&&current.SourceStamp!=p.SourceStamp)throw new InvalidOperationException("事实文档已被外部修改；保留输入，重新加载并核对。");
  if(current!=null&&current.ExternalChanges.Count>0&&!acceptExternal)throw new InvalidOperationException("Markdown存在外部变更。已读取但未经本人核对，不能自动同步批准意图。");
  if(current?.Format=="markdown-v3"&&CheckTemplate(current).Any(s=>s.StartsWith("缺少 ")||s.StartsWith("规范身份")))throw new InvalidOperationException("项目规范有缺项/身份冲突，不能声称同步完整；先检查并修复。");
  if(current?.Format=="legacy-json"&&!upgrade)throw new InvalidOperationException("此项目仍使用旧JSON。先预览并升级为同源Markdown。");
  if(current==null||upgrade)InstallTemplate(p.Id);
  var next=Clone(p);next.Version=expected+1;next.Format="markdown-v3";next.Events.Add(DateTime.UtcNow.ToString("u")+" "+action);
  next.Events=next.Events.TakeLast(100).ToList();
  string snapshot="S-"+next.Version+"-"+Guid.NewGuid().ToString("N")[..12];var documents=SnapshotDocuments(next);
  foreach(var pair in documents){WriteDurable(SnapshotPath(p.Id,snapshot,pair.Key),pair.Value);WriteDurable(ProjectPath(p.Id,"archive/document-baselines/"+snapshot+"/"+pair.Key),pair.Value);}
  var hashes=documents.ToDictionary(x=>x.Key,x=>Hash(Encoding.UTF8.GetBytes(x.Value)));
  var templateHashes=new Dictionary<string,string>();
  foreach(var relative in new[]{"repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}){
   var content=ReadUtf8(ProjectPath(p.Id,relative));templateHashes[relative]=Hash(Encoding.UTF8.GetBytes(content));WriteDurable(ProjectPath(p.Id,"archive/document-baselines/"+snapshot+"/ROOT/"+relative),content);
  }
  string pointer=MarkdownCodec.Document("当前事实入口",new(){{"project_id",p.Id},{"record_version",next.Version},{"demo",Demo},{"snapshot",snapshot},{"files",documents.Keys.ToArray()},{"hashes",hashes},{"template_hashes",templateHashes}},new(){{"阅读顺序","先读 PROJECT.md、STATE.md 和当前 tasks/<task-id>.md；按相关性补 knowledge。快照路径：snapshots/"+snapshot+"。其他快照是历史，不是当前要求。"}});
  var pending=ProjectPath(p.Id,"repo/docs/current-"+Guid.NewGuid().ToString("N")+".pending.md");WriteDurable(pending,pointer);
  if(upgrade)File.Copy(LegacyPath(p.Id),ProjectPath(p.Id,"archive/legacy-records-"+Guid.NewGuid().ToString("N")+".json"));
  var marker=ProjectPath(p.Id,"repo/docs/format-v3.marker");
  var journal=ProjectPath(p.Id,"repo/docs/journal-"+Guid.NewGuid().ToString("N")+".pending.md");WriteDurable(journal,pointer);
  if(File.Exists(marker))File.Replace(journal,marker,null);else File.Move(journal,marker);
  if(upgrade){
   var readme=ProjectPath(p.Id,"README.md");var old=ReadUtf8(readme);
   if(old.Contains("[repo/docs/records.json](repo/docs/records.json)")){
    File.Copy(readme,ProjectPath(p.Id,"archive/readme-before-upgrade-"+Guid.NewGuid().ToString("N")+".md"));
    var readmePending=ProjectPath(p.Id,"readme-"+Guid.NewGuid().ToString("N")+".pending.md");
    WriteDurable(readmePending,old.Replace("[repo/docs/records.json](repo/docs/records.json)","[repo/docs/CURRENT.md](repo/docs/CURRENT.md)"));
    File.Replace(readmePending,readme,null);
   }
  }
  if(current?.Format=="legacy-json"){if(File.Exists(PointerPath(p.Id))||Hash(File.ReadAllBytes(LegacyPath(p.Id)))!=current.SourceStamp)throw new InvalidOperationException("Legacy source changed before commit.");}
  else if(current!=null)RequireFresh(current);
  else if(File.Exists(PointerPath(p.Id)))throw new InvalidOperationException("提交前项目入口出现，拒绝覆盖。");
  if(File.Exists(PointerPath(p.Id)))File.Replace(pending,PointerPath(p.Id),PreviousPath(p.Id));else File.Move(pending,PointerPath(p.Id));
  var saved=LoadFacts(p.Id);p.Version=saved.Version;p.Events=saved.Events;p.Format=saved.Format;p.SourceStamp=saved.SourceStamp;p.ExternalChanges=saved.ExternalChanges;
 }
 public string ExternalDiff(ProjectRecord p) {
  var m=Pointer(p.Id,PointerPath(p.Id));string snapshot=MarkdownCodec.String(m,"snapshot");
  return string.Join("\n\n",p.ExternalChanges.Select(file=>{
   bool rootFile=file.StartsWith("repo/");string baseline=ProjectPath(p.Id,"archive/document-baselines/"+snapshot+"/"+(rootFile?"ROOT/":"")+file);
   return "--- "+file+"\n原基线（历史，仅用于差异）:\n"+(File.Exists(baseline)?ReadUtf8(baseline):"基线副本缺失，不能推测")+"\n当前可编辑事实:\n"+ReadUtf8(rootFile?ProjectPath(p.Id,file):SnapshotPath(p.Id,snapshot,file));
  }));
 }
 public void AcceptExternal(ProjectRecord p) {
  var next=Clone(p);
  foreach(var k in next.Knowledge.Where(k=>next.ExternalChanges.Contains("knowledge/"+k.Id+".md")))k.Verification="needs_verification";
  SaveFacts(next,next.Version,"本人核对并接受外部文档变更；相关知识待复核",false,true);
  p.Version=next.Version;p.Events=next.Events;p.Format=next.Format;p.SourceStamp=next.SourceStamp;p.ExternalChanges=next.ExternalChanges;p.Knowledge=next.Knowledge;
 }
 public bool HasMissingRelated(ProjectRecord p,WorkTask t)=>t.RelatedFiles.Any(raw=>!File.Exists(ProjectPath(p.Id,NormalizeRelated(raw))));
 public void UpgradeMarkdown(ProjectRecord p) {
  if(p.Format!="legacy-json")throw new InvalidOperationException("已使用Markdown，无需重复升级。");
  SaveFacts(p,p.Version,"用户确认：保留旧JSON并升级Markdown",true);
 }
 void InstallTemplate(string id) {
  var templates=new Dictionary<string,string>{
   ["README.md"]="# 项目入口\n\n[当前事实](repo/docs/CURRENT.md) · [工作规范](repo/docs/WORKFLOW.md) · [测试入口](repo/docs/TESTING.md)。\n此文件只索引。原始资料在docs；不要复制现行知识。\n",
   ["repo/AGENTS.md"]="# 精简接手入口\nproject_id: "+id+"\n仅允许当前项目根目录。\n先读 docs/CURRENT.md 指向的 PROJECT、STATE 与当前任务卡。\n按需读有关knowledge；不要默认通读全部历史。\n批准意图/观察实现/待批准分开；新要求未实现仍有效。\n任务开始和验收检查来源、版本、相关文件与证据。\n代码变化只触发复核，不改需求掩盖错误。\n安装/删除/其他项目访问不在权限中。\n顺序接力优先，交接更新同一任务卡。\n验证范围和未运行项如实记录。\n",
   ["repo/CLAUDE.md"]="# 薄适配\n先读AGENTS.md，再按CURRENT指定的当前任务加载。\n",
   ["repo/GEMINI.md"]="# 薄适配\n先读AGENTS.md，再按CURRENT指定的当前任务加载。\n",
   ["repo/docs/WORKFLOW.md"]="# 工作规范\nproject_id: "+id+"\n\n开始：按CURRENT加载当前任务，核对批准意图、实现、未核实与冲突。精简加载；影响不明升级。\n事实：Markdown快照由CURRENT唯一指定，App与AI读取同一正文；旧JSON仅历史输入。\n编辑：元数据为JSON兼容YAML；正文只改wb字段标记内内容，保留标记/身份/版本。任何外部编辑会使来源指纹变化。未知新增段落请保留另行提案，不让App覆盖正文。\n批准要求修改必须由本人明确确认；AI只能提出待批准方案。\n生命周期active/superseded/retired与verified/needs_verification/unknown独立，最后核实时间单独保留。\n接手：旧AI停写，新AI核对任务包、版本、相关文件与证据；同任务卡增量交接。\n验收：没有证据或Git锚点写unknown；文件指纹一致不等于测试语义通过。\n收尾：保留正式测试/失败证据；输出进runs，发布进releases。清理仅精确预览，不执行安装/删除/迁移。\n",
   ["repo/docs/TESTING.md"]="# 项目测试入口\nproject_id: "+id+"\n\n真实代码测试命令：尚未登记，unknown。\n工作台机械检查：界面“检查项目规范”；检查文档身份、目录、版本与相关来源文件。\n记录验证文件于本项目runs，并在任务内关联相对路径。文件存在/哈希通过不表示真实测试已通过。\n必须记录命令、结果、实际所测代码与输入版本；未运行不能写通过。\n",
   ["repo/docs/DECISIONS.md"]="# 决策入口\nproject_id: "+id+"\n\n项目决策作为knowledge记录，role=已批准意图或待批准方案，并保留来源及替代关系；不要在此复制知识正文。\n"
  };
  foreach(var pair in templates){var path=ProjectPath(id,pair.Key);if(!File.Exists(path))WriteDurable(path,pair.Value);}
 }
 public string TemplateText(ProjectRecord p)=>"事实入口："+MarkdownEntry(p.Id)+"\n\n"+(File.Exists(ProjectPath(p.Id,"repo/AGENTS.md"))?ReadUtf8(ProjectPath(p.Id,"repo/AGENTS.md")):"缺少AGENTS")+ "\n\n"+(File.Exists(PointerPath(p.Id))?ReadUtf8(PointerPath(p.Id)):"待用户升级Markdown");
 public string[] CheckTemplate(ProjectRecord p) {
  var issues=new List<string>();
  foreach(var relative in new[]{"README.md","repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md","repo/docs/DECISIONS.md"})if(!File.Exists(ProjectPath(p.Id,relative)))issues.Add("缺少 "+relative);
  foreach(var relative in new[]{"repo","docs","assets","data","runs","releases","archive"})if(!Directory.Exists(ProjectPath(p.Id,relative)))issues.Add("缺少目录 "+relative);
  foreach(var relative in new[]{"repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}){
   var path=ProjectPath(p.Id,relative);if(File.Exists(path)&&!ReadUtf8(path).Contains("project_id: "+p.Id))issues.Add("规范身份冲突 "+relative);
  }
  if(p.Format!="markdown-v3")issues.Add("尚未升级Markdown事实源");
  else{try{LoadFacts(p.Id);}catch(Exception ex){issues.Add("事实源无效："+ex.Message);}}
  foreach(var t in p.Tasks)foreach(var file in t.RelatedFiles){try{var path=ProjectPath(p.Id,NormalizeRelated(file));if(!File.Exists(path))issues.Add(t.Id+" 来源缺失 "+file);}catch(Exception ex){issues.Add(t.Id+" 来源无效："+ex.Message);}}
  return issues.ToArray();
 }
 public void FillMissingTemplate(ProjectRecord p) {
  using var templateLock=new FileStream(ProjectPath(p.Id,"repo/docs/write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  if(LoadFacts(p.Id).SourceStamp!=p.SourceStamp)throw new InvalidOperationException("来源已变化，重新加载再预览。");
  InstallTemplate(p.Id);
 }
 public void RequireFresh(ProjectRecord p){
  if(LoadFacts(p.Id).SourceStamp!=p.SourceStamp)throw new InvalidOperationException("文档来源已变化，请刷新并核对，旧窗口内容不能生成新证据或接手包。");
 }
 public long FactStorageBytes(string id){
  var p=LoadFacts(id);return BackupFactFiles(p).Select(f=>ProjectPath(id,f)).Where(File.Exists).Sum(f=>new FileInfo(f).Length);
 }
 public string NormalizeRelated(string relative) {
  var value=relative.Trim().Replace('\\','/');
  if(value==""||Path.IsPathRooted(value)||value.Split('/').Any(x=>x==".."||x=="."||x=="")||value.Contains(':'))throw new InvalidOperationException("相关文件必须是当前项目内普通相对路径。");
  if(value.Split('/').Any(x=>x.EndsWith(".")||x.EndsWith(" ")||x.IndexOfAny(Path.GetInvalidFileNameChars())>=0||Regex.IsMatch(x,"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])([.]|$)",RegexOptions.IgnoreCase)))throw new InvalidOperationException("拒绝Windows别名/设备文件路径。");
  if(value.StartsWith("repo/docs/snapshots/",StringComparison.OrdinalIgnoreCase)||value.StartsWith("repo/docs/CURRENT",StringComparison.OrdinalIgnoreCase)||value.EndsWith("write.lock",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("快照文档由事实入口自动关联，不能重复登记。");
  if(ExcludedAssetPath(value)!="")throw new InvalidOperationException("敏感/缓存路径不能登记为任务来源");
  return value;
 }
 public string RelatedFingerprint(ProjectRecord p,WorkTask t) {
  var values=new List<string>();
  foreach(var raw in t.RelatedFiles.Distinct().OrderBy(x=>x,StringComparer.Ordinal)) {
   var relative=NormalizeRelated(raw);var path=ProjectPath(p.Id,relative);
   values.Add(relative+"="+(File.Exists(path)?Hash(File.ReadAllBytes(path)):"MISSING"));
  }
  foreach(var relative in new[]{"repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}){
   var path=ProjectPath(p.Id,relative);values.Add(relative+"="+(File.Exists(path)?Hash(File.ReadAllBytes(path)):"MISSING"));
  }
  return string.Join("\n",values);
 }
 IEnumerable<Fact> Relevant(ProjectRecord p,WorkTask t)=>p.Knowledge.Where(k=>k.Lifecycle=="active"&&(k.Scope=="项目"||k.Scope=="全项目"||k.Scope==t.Scope||t.Scope.StartsWith(k.Scope.TrimEnd('/')+"/",StringComparison.Ordinal)));
 public string SourceSummary(ProjectRecord p,WorkTask t) {
  if(p.Format!="markdown-v3")return "事实源：旧JSON（待用户升级）；无同源Markdown模板。";
  var m=Pointer(p.Id,PointerPath(p.Id));var snapshot=MarkdownCodec.String(m,"snapshot");
  var relevant=new[]{"PROJECT.md","STATE.md","tasks/"+t.Id+".md"}.Concat(Relevant(p,t).Select(k=>"knowledge/"+k.Id+".md"));
  return "外部变更："+(p.ExternalChanges.Count==0?"无":string.Join("、",p.ExternalChanges)+"；未经本人核对，不作为新授权")+"\n事实入口：repo/docs/CURRENT.md\nMarkdown来源版本："+snapshot+"\n事实工作区指纹："+p.SourceStamp+"\n"+
   string.Join("\n",relevant.Select(file=>"repo/docs/snapshots/"+snapshot+"/"+file+" SHA256="+Hash(File.ReadAllBytes(SnapshotPath(p.Id,snapshot,file)))))+
   "\n登记的相关文件 / 固定规范：\n"+RelatedFingerprint(p,t)+"\n\n"+ReadGit(p,t).Describe();
 }
 public ProjectRecord RestoreFacts(string id) {
  using var writeLock=new FileStream(ProjectPath(id,"repo/docs/write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var priorPath=File.Exists(PreviousPath(id))?PreviousPath(id):ProjectPath(id,"repo/docs/format-v3.marker");
  var prior=ReadSnapshot(id,priorPath);var pointer=PointerPath(id);
  if(File.Exists(pointer))File.Copy(pointer,ProjectPath(id,"archive/before-restore-"+Guid.NewGuid().ToString("N")+".md"));
  var oldText=ReadUtf8(priorPath);var pending=ProjectPath(id,"repo/docs/restore-"+Guid.NewGuid().ToString("N")+".pending.md");WriteDurable(pending,oldText);
  if(File.Exists(pointer))File.Replace(pending,pointer,ProjectPath(id,"archive/restored-current-"+Guid.NewGuid().ToString("N")+".md"));else File.Move(pending,pointer);
  return LoadFacts(id);
 }
 public string[] BackupFactFiles(ProjectRecord p) {
  if(p.Format!="markdown-v3")return new[]{"README.md","repo/docs/records.json","repo/docs/records.previous.json"};
  var list=new List<string>{"README.md","repo/AGENTS.md","repo/CLAUDE.md","repo/GEMINI.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md","repo/docs/DECISIONS.md","repo/docs/CURRENT.md","repo/docs/format-v3.marker"};
  foreach(var pointer in new[]{PointerPath(p.Id),PreviousPath(p.Id)}){
   if(!File.Exists(pointer))continue;
   var m=Pointer(p.Id,pointer);string snapshot=MarkdownCodec.String(m,"snapshot");
   foreach(var file in MarkdownCodec.Strings(m,"files")){
    list.Add("repo/docs/snapshots/"+snapshot+"/"+file);
    var baseline="archive/document-baselines/"+snapshot+"/"+file;if(File.Exists(ProjectPath(p.Id,baseline)))list.Add(baseline);
   }
   foreach(var relative in new[]{"repo/AGENTS.md","repo/docs/WORKFLOW.md","repo/docs/TESTING.md"}){
    var baseline="archive/document-baselines/"+snapshot+"/ROOT/"+relative;if(File.Exists(ProjectPath(p.Id,baseline)))list.Add(baseline);
   }
  }
  if(File.Exists(PreviousPath(p.Id)))list.Add("repo/docs/CURRENT.previous.md");
  return list.Distinct().ToArray();
 }
}
