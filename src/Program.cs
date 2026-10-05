using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Automation;
using WButton=System.Windows.Controls.Button;

namespace Workbench;
public class Fact {
 public string Id {get;set;}="K-"+Guid.NewGuid().ToString("N")[..8];
 public string Text {get;set;}="";
 public string Role {get;set;}="待批准方案";
 public string Source {get;set;}="";
 public string Scope {get;set;}="项目";
 public string Lifecycle {get;set;}="active";
 public string Verification {get;set;}="unknown";
 public string Revision {get;set;}="R1";
 public string Supersedes {get;set;}="";
 public string ConflictWith {get;set;}="";
 public string VerifiedAt {get;set;}="";
}
public class WorkTask {
 public string Id {get;set;}="T-"+Guid.NewGuid().ToString("N")[..8];
 public string Goal {get;set;}="";
 public string Owner {get;set;}="未分配";
 public string Status {get;set;}="planned";
 public string Scope {get;set;}="repo/docs";
 public string Done {get;set;}="";
 public string NotDone {get;set;}="";
 public string Next {get;set;}="";
 public string Evidence {get;set;}="";
 public string EvidenceHash {get;set;}="";
 public string VerifiedAnchor {get;set;}="";
 public string VerifiedAt {get;set;}="";
 public List<string> RelatedFiles {get;set;}=new();
 public string WorktreeAndBranch {get;set;}="当前任务包只读复核Git；此字段不代表历史验证";
 public string LastVerifiedCodeCommit {get;set;}="unknown";
}
public class ProjectRecord {
 public string Id {get;set;}="";
 public string Name {get;set;}="";
 public string Goal {get;set;}="";
 public bool Demo {get;set;}
 public int Version {get;set;}
 public string Format {get;set;}="";
 public string SourceStamp {get;set;}="";
 public List<string> ExternalChanges {get;set;}=new();
 public List<WorkTask> Tasks {get;set;}=new();
 public List<Fact> Knowledge {get;set;}=new();
 public List<string> Events {get;set;}=new();
}
public record Entry(string Id,string Name,string Error);
public partial class Workspace {
 public string Root {get;}
 public bool Demo {get;}
 public static readonly JsonSerializerOptions Json=new(){WriteIndented=true};
 public Workspace(string root,bool demo=false) {
  Root=Path.GetFullPath(root);Demo=demo;
  if(!File.Exists(Path.Combine(Root,"project.json")))throw new InvalidOperationException("缺少独立项目标记。");
  Directory.CreateDirectory(Safe("data/"+Mode+"/Projects"));
 }
 public string Mode=>Demo?"examples":"local";
 public string Safe(string relative) {
  var path=Path.GetFullPath(Path.Combine(Root,relative));
  if(!path.StartsWith(Root+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("路径越界。");
  var directory=new DirectoryInfo(Path.GetDirectoryName(path)!);
  while(directory!=null&&directory.FullName.Length>=Root.Length){
   if(directory.Exists&&(directory.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("拒绝访问链接目录。");
   directory=directory.Parent;
  }
  if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("拒绝访问链接文件。");
  return path;
 }
 string ProjectPath(string id,string relative) {
  if(!System.Text.RegularExpressions.Regex.IsMatch(id,"^P-[a-f0-9]{12}$"))throw new InvalidOperationException("非法项目标识。");
  var prefix=Safe("data/"+Mode+"/Projects/"+id)+Path.DirectorySeparatorChar;
  var result=Safe("data/"+Mode+"/Projects/"+id+"/"+relative);
  if(!result.StartsWith(prefix,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("路径越出当前项目。");
  return result;
 }
 public string RecordPath(string id)=>PointerPath(id);
 public Entry[] List() {
  var folder=Safe("data/"+Mode+"/Projects");
  return Directory.GetDirectories(folder).Where(d=>(File.GetAttributes(d)&FileAttributes.ReparsePoint)==0)
   .Select(d=>Path.GetFileName(d)).Where(id=>System.Text.RegularExpressions.Regex.IsMatch(id,"^P-[a-f0-9]{12}$"))
   .Select(id=>{try{var p=Load(id);return new Entry(id,p.Name,"");}catch(Exception ex){return new Entry(id,id,"记录无法读取："+ex.Message);}}).OrderBy(e=>e.Name,StringComparer.Ordinal).ToArray();
 }
 public ProjectRecord Load(string id)=>LoadFacts(id);
  public static void FinishFactEdit(Fact? before,Fact after) {
  if(before==null){after.VerifiedAt=after.Verification=="verified"?DateTime.UtcNow.ToString("u"):"";return;}
  bool changed=before.Text!=after.Text||before.Source!=after.Source||before.Scope!=after.Scope||before.Revision!=after.Revision||before.Role!=after.Role;
  if(changed)after.Verification="needs_verification";
  after.VerifiedAt=!changed&&after.Verification=="verified"&&before.Verification!="verified"?DateTime.UtcNow.ToString("u"):before.VerifiedAt;
 }
 public static ProjectRecord Clone(ProjectRecord p)=>JsonSerializer.Deserialize<ProjectRecord>(JsonSerializer.Serialize(p))!;
 public static void Validate(ProjectRecord p) {
  if(string.IsNullOrWhiteSpace(p.Name)||string.IsNullOrWhiteSpace(p.Goal))throw new InvalidDataException("项目名称和目标必填。");
  if(p.Tasks==null||p.Knowledge==null||p.Events==null)throw new InvalidDataException("记录字段缺失。");
  if(p.Tasks.Select(t=>t.Id).Distinct().Count()!=p.Tasks.Count||p.Knowledge.Select(k=>k.Id).Distinct().Count()!=p.Knowledge.Count)throw new InvalidDataException("重复记录标识。");
  foreach(var t in p.Tasks)if(string.IsNullOrWhiteSpace(t.Goal)||!new[]{"planned","running","blocked","review","cleanup_pending","done"}.Contains(t.Status))throw new InvalidDataException("任务目标或状态无效。");
  foreach(var k in p.Knowledge)if(string.IsNullOrWhiteSpace(k.Text)||string.IsNullOrWhiteSpace(k.Source)||!new[]{"已批准意图","观察实现","待批准方案","复用经验"}.Contains(k.Role)||!new[]{"active","superseded","retired"}.Contains(k.Lifecycle)||!new[]{"verified","needs_verification","unknown"}.Contains(k.Verification))throw new InvalidDataException("知识正文、来源或状态无效。");
 }
 public ProjectRecord Create(string name,string goal) {
  var p=new ProjectRecord{Id="P-"+Guid.NewGuid().ToString("N")[..12],Name=name.Trim(),Goal=goal.Trim(),Demo=Demo};
  Validate(p);
  var folder=ProjectPath(p.Id,"repo/docs");Directory.CreateDirectory(folder);
  foreach(var d in new[]{"docs","assets","data","runs","releases","archive"})Directory.CreateDirectory(ProjectPath(p.Id,d));
  Save(p,0,"创建项目");

  return p;
 }
 public void Save(ProjectRecord p,int expected,string action)=>SaveFacts(p,expected,action);
 public bool CanRestore(string id)=>File.Exists(PreviousPath(id))||File.Exists(ProjectPath(id,"repo/docs/format-v3.marker"));
 public ProjectRecord Restore(string id)=>RestoreFacts(id);
 public string Anchor(ProjectRecord p,WorkTask t) {
  var value=new {p.Id,p.Goal,Task=new{t.Id,t.Goal,t.Scope},Related=RelatedFingerprint(p,t),Git=GitFingerprint(p,t),Knowledge=Relevant(p,t).Select(k=>new{k.Id,k.Text,k.Source,k.Role,k.Revision,k.ConflictWith,k.Verification})};
  return Hash(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(value)));
 }
 static string Hash(byte[] data)=>Convert.ToHexString(SHA256.HashData(data));
 public string Verify(ProjectRecord p,WorkTask t) {
  try{RequireFresh(p);}catch{return "needs_verification · 文档变化或不可读，请刷新项目文档";}
  if(p.ExternalChanges.Any(f=>f=="PROJECT.md"||f=="tasks/"+t.Id+".md"||f.StartsWith("repo/")||Relevant(p,t).Any(k=>f=="knowledge/"+k.Id+".md")))return "needs_verification · 外部文档变化待本人核对";
  var git=ReadGit(p,t);if(new[]{"unavailable","unsupported","changed-during-read"}.Contains(git.State))return "unknown · Git来源不可核对";
  if(HasMissingRelated(p,t))return "unknown · 登记的相关来源文件缺失";
  if(t.Evidence=="")return "unknown · 未提供验证证据";
  try{
   NormalizeArchiveName(t.Evidence);if(ExcludedAssetPath(t.Evidence)!="")return "unknown · 证据路径被安全策略排除";
   var path=ProjectPath(p.Id,t.Evidence);
   if(!t.Evidence.Replace('\\','/').StartsWith("runs/",StringComparison.Ordinal))return "unknown · 证据必须位于当前项目 runs";
   if(!File.Exists(path))return "unknown · 证据文件不存在";
   if(t.VerifiedAnchor!=Anchor(p,t)||t.EvidenceHash!=Hash(File.ReadAllBytes(path)))return "needs_verification · 来源或证据变化";
   return "verified · 文件与记录锚点一致（不代表语义测试通过）";
  }catch{return "unknown · 证据路径不可用";}
 }
 public void RecordEvidence(ProjectRecord p,WorkTask t,string evidence) {
  RequireFresh(p);
  if(HasMissingRelated(p,t))throw new InvalidOperationException("登记的相关来源文件缺失，不能确认验证。");
  var normalized=evidence.Replace('\\','/');
  if(!normalized.StartsWith("runs/",StringComparison.Ordinal)||normalized.Contains("../"))throw new InvalidOperationException("仅支持当前项目 runs 内的相对证据路径。");
  NormalizeArchiveName(normalized);if(ExcludedAssetPath(normalized)!="")throw new InvalidOperationException("证据路径被安全策略排除");
  var git=ReadGit(p,t);if(new[]{"unavailable","unsupported","changed-during-read"}.Contains(git.State))throw new InvalidOperationException("Git来源不可核对，不能记录确认");
  var path=ProjectPath(p.Id,normalized);if(!File.Exists(path))throw new InvalidOperationException("证据不存在；未记录成功。");
  t.Evidence=normalized;t.EvidenceHash=Hash(File.ReadAllBytes(path));t.VerifiedAnchor=Anchor(p,t);t.VerifiedAt=DateTime.UtcNow.ToString("u");t.LastVerifiedCodeCommit=git.State=="observed"?git.Commit:"unknown";
 }
 public string Package(ProjectRecord p,WorkTask t) {
  RequireFresh(p);string initialAnchor=Anchor(p,t);var packageGit=ReadGit(p,t);
  var active=Relevant(p,t).Select(k=>{if(!p.ExternalChanges.Contains("knowledge/"+k.Id+".md"))return k;var observed=JsonSerializer.Deserialize<Fact>(JsonSerializer.Serialize(k))!;observed.Verification="needs_verification";return observed;}).ToArray();
  string Section(string label,IEnumerable<Fact> facts)=>"\n## "+label+"\n"+(facts.Any()?string.Join("\n",facts.Select(k=>$"- {k.Id} [{k.Role} / {k.Verification}] {k.Text}\n  来源：{k.Source}；范围：{k.Scope}；版本：{k.Revision}")):"无相关记录");
  var package=SourceSummary(p,t)+"\n\n# "+(Demo?"演示":"本地")+" AI 接手与交接包\nproject_id: "+p.Id+"\ntask_id: "+t.Id+"\n项目目标："+p.Goal+"\n当前任务："+t.Goal+"\n负责人："+t.Owner+"\n状态："+t.Status+"\n允许范围：本项目 "+t.Scope+"\n记录版本："+p.Version+"\n来源指纹："+Anchor(p,t)+"\nGit："+packageGit.State+"；分支："+packageGit.Branch+"；提交："+packageGit.Commit+"；登记范围文件："+packageGit.Files.Count+"\n最后证据提交："+t.LastVerifiedCodeCommit+"\n验证："+Verify(p,t)+
   Section("已核实记录",active.Where(k=>k.Verification=="verified"&&k.ConflictWith==""&&k.Role!="待批准方案"))+
   Section("待核实与待批准",active.Where(k=>k.Verification!="verified"||k.Role=="待批准方案"))+
   Section("显式冲突 · 并列保留来源",active.Where(k=>k.ConflictWith!=""))+
   "\n\n已做："+t.Done+"\n未做："+t.NotDone+"\n下一步："+t.Next+"\n证据："+(t.Evidence==""?"未提供":t.Evidence)+"\n证据SHA256："+t.EvidenceHash+"\n最后核对："+t.VerifiedAt+
   "\n\n接手前：检查意图/实现差异，待批准方案不能当要求；代码变化触发重核，不能替自身错误修改需求。\n取舍：顺序接力优先；本App未启动或约束外部代理。\n权限：不读取其他项目，不安装、删除、迁移真实环境。\n验收：文件指纹一致仅说明关联有效；实际测试结论须阅读证据，当前不能自动判断全面通过。\n";
  RequireFresh(p);if(initialAnchor!=Anchor(p,t))throw new InvalidOperationException("生成期间相关来源改变，请刷新后重建。");
  return package;
 }
 public string Export(ProjectRecord p,WorkTask t,string preview,string anchor) {
  using var exportLock=new FileStream(ProjectPath(p.Id,"repo/docs/write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var fresh=Load(p.Id);var current=fresh.Tasks.First(x=>x.Id==t.Id);
  if(Anchor(fresh,current)!=anchor||Package(fresh,current)!=preview)throw new InvalidOperationException("预览已过期，请重新生成。");
  var folder=ProjectPath(p.Id,"runs/exports");Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,"handoff-"+t.Id+"-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")[..6]+".md");
  using var file=new FileStream(path,FileMode.CreateNew);file.Write(Encoding.UTF8.GetBytes(preview));return path;
 }
 public string Backup(ProjectRecord p) {
  using var backupLock=new FileStream(ProjectPath(p.Id,"repo/docs/write.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
  var fresh=Load(p.Id);var folder=Safe("runs/exports");Directory.CreateDirectory(folder);
  var path=Path.Combine(folder,"backup-"+p.Id+"-v"+fresh.Version+"-"+Guid.NewGuid().ToString("N")[..6]+".zip");
  using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
  foreach(var relative in BackupFactFiles(fresh)){
   var source=ProjectPath(p.Id,relative);if(File.Exists(source))zip.CreateEntryFromFile(source,relative);
  }
  var entry=zip.CreateEntry("manifest.json");using var writer=new StreamWriter(entry.Open());
  writer.Write(JsonSerializer.Serialize(new{project_id=p.Id,version=fresh.Version,exported_at=DateTime.UtcNow,scope="canonical editable Markdown snapshots and template; previous snapshot; evidence/assets not included",sha256=Hash(File.ReadAllBytes(RecordPath(p.Id)))},Json));
  return path;
 }
 public void SeedExamples(){
  if(!Demo||List().Length>0)return;
  foreach(var name in new[]{"星图笔记（虚构）","轨道清单（虚构）"}){
   var p=Create(name,"验证本项目内的知识与任务闭环");
   p.Tasks.Add(new WorkTask{Goal="按已批准 R2 支持 CSV 和 JSON 导出",NotDone="JSON 尚未实现；无真实测试证据",Next="核对需求与实现差异，再补充验证"});
   var approved=new Fact{Id="K-R2",Text="支持 CSV 和 JSON。",Role="已批准意图",Source="虚构用户确认 R2",Revision="R2",Supersedes="K-R1"};
   p.Knowledge.Add(approved);
   p.Knowledge.Add(new Fact{Id="K-OBS",Text="虚构实现仍只有 CSV。",Role="观察实现",Source="虚构代码快照 r1",Verification="needs_verification",ConflictWith="K-R2"});
   p.Knowledge.Add(new Fact{Id="K-R1",Text="旧版仅 CSV。",Role="已批准意图",Source="虚构历史规格 R1",Lifecycle="superseded",Verification="verified"});
   p.Knowledge.Add(new Fact{Id="K-P",Text="以后考虑自动并行。",Source="未批准提案",Role="待批准方案"});
   Save(p,p.Version,"写入虚构示例");
  }
 }
}
public enum LeaveDecision {Save,Discard,Stay}
public class EditorWindow:Window {
 readonly Dictionary<string,TextBox> fields=new();
 readonly Dictionary<string,ComboBox> choices=new();
 readonly TextBlock error=new(){Foreground=Brushes.Firebrick,TextWrapping=TextWrapping.Wrap};
 readonly Func<Dictionary<string,string>,bool> commit;
 readonly string baseline;
 bool saved;
 public EditorWindow(string title,(string Key,string Label,string Value,string[]? Options)[] schema,Func<Dictionary<string,string>,bool> action) {
  Title=title;Width=640;Height=660;MinWidth=500;MinHeight=450;WindowStartupLocation=WindowStartupLocation.CenterOwner;Background=Brushes.White;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;
  commit=action;
  var dock=new DockPanel{Margin=new Thickness(24)};
  var buttons=new WrapPanel{Margin=new Thickness(0,15,0,0)};DockPanel.SetDock(buttons,Dock.Bottom);dock.Children.Add(buttons);
  var save=new WButton{Content="保存",Padding=new Thickness(20,10,20,10),IsDefault=true,Margin=new Thickness(0,0,15,0)};
  save.Click+=(_,__)=>{if(TrySave())Close();};
  var cancel=new WButton{Content="取消",Padding=new Thickness(20,10,20,10),IsCancel=true};cancel.Click+=(_,__)=>Close();
  buttons.Children.Add(save);buttons.Children.Add(cancel);
  DockPanel.SetDock(error,Dock.Bottom);dock.Children.Add(error);
  var panel=new StackPanel();dock.Children.Add(new ScrollViewer{Content=panel,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
  foreach(var field in schema) {
   panel.Children.Add(new TextBlock{Text=field.Label,Margin=new Thickness(0,12,0,6),FontWeight=FontWeights.SemiBold});
   if(field.Options!=null){
    var box=new ComboBox{ItemsSource=field.Options,SelectedItem=field.Value,Padding=new Thickness(8)};choices[field.Key]=box;AutomationProperties.SetName(box,field.Label);panel.Children.Add(box);
   }else{
    var box=new TextBox{Text=field.Value,Padding=new Thickness(10),TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,MinHeight=42,MaxHeight=125,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};
    fields[field.Key]=box;AutomationProperties.SetName(box,field.Label);panel.Children.Add(box);
   }
  }
  Content=dock;baseline=Signature();
  Closing+=(_,e)=>{if(saved||!Dirty)return;var decision=MessageBox.Show(this,"存在未保存修改。是：保存并关闭；否：放弃输入；取消：留在此处。","保留未保存输入",MessageBoxButton.YesNoCancel,MessageBoxImage.Question);e.Cancel=!MayLeave(decision==MessageBoxResult.Yes?LeaveDecision.Save:decision==MessageBoxResult.No?LeaveDecision.Discard:LeaveDecision.Stay);};
 }
 Dictionary<string,string> Values()=>fields.ToDictionary(x=>x.Key,x=>x.Value.Text.Trim()).Concat(choices.Select(x=>new KeyValuePair<string,string>(x.Key,x.Value.SelectedItem?.ToString()??""))).ToDictionary(x=>x.Key,x=>x.Value);
 string Signature()=>JsonSerializer.Serialize(Values());
 public bool Dirty=>Signature()!=baseline;
 public bool TrySave(){if(saved)return true;try{if(!commit(Values()))return false;saved=true;return true;}catch(Exception ex){error.Text="保存未完成："+ex.Message+" 输入已保留。";return false;}}
 public bool MayLeave(LeaveDecision choice)=>choice==LeaveDecision.Discard||(choice==LeaveDecision.Save&&TrySave());
 public void TestSet(string key,string text)=>fields[key].Text=text;
 public string TestRead(string key)=>fields[key].Text;
 public string Error=>error.Text;
}
public partial class MainWindow:Window {
 readonly Workspace workspace;
 readonly ComboBox picker=new(){MinWidth=230};
 readonly StackPanel body=new();
 readonly TextBlock notice=new(){TextWrapping=TextWrapping.Wrap};
 readonly Dictionary<string,WButton> actions=new();
 Entry[] entries=Array.Empty<Entry>();ProjectRecord? project;string selected="";string page="项目总览";string taskId="";string filter="";bool history;
 string preview="",previewAnchor="";
 public MainWindow(Workspace w) {
  workspace=w;Title="AI 项目工作台 · 0.3 单机 MVP"+(w.Demo?" · 演示":"");Width=Math.Min(1240,SystemParameters.WorkArea.Width-30);Height=Math.Min(840,SystemParameters.WorkArea.Height-30);MinWidth=850;MinHeight=480;WindowStartupLocation=WindowStartupLocation.CenterScreen;FontFamily=new FontFamily("Microsoft YaHei UI");FontSize=14;Background=Color("#F5F7FB");
  var root=new Grid{Background=Color("#F5F7FB")};root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(200)});root.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});Content=root;
  var nav=new StackPanel{Margin=new Thickness(18,28,18,18)};root.Children.Add(new Border{Background=Color("#14233B"),Child=new ScrollViewer{Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto}});
  nav.Children.Add(Text("AI 项目工作台",21,"#FFFFFF",true));nav.Children.Add(Text("目标 · 下一步 · 风险",12,"#AAB8D1"));nav.Children.Add(new Border{Height=25});
  foreach(var label in new[]{"项目总览","任务与接手","知识与冲突","交接与备份","环境与边界"}) {
   var b=Button(label,()=>Show(label));b.Background=Color("#20344F");b.Foreground=Brushes.White;b.HorizontalContentAlignment=HorizontalAlignment.Left;nav.Children.Add(b);
  }
  nav.Children.Add(new Border{Height=35});nav.Children.Add(Text(w.Demo?"演示数据独立存储":"本地空白模式\n仅管理本项目沙盒",12,"#AAB8D1"));
  var main=new Grid{Margin=new Thickness(28,24,28,18)};Grid.SetColumn(main,1);root.Children.Add(main);
  main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});main.RowDefinitions.Add(new RowDefinition{Height=new GridLength(1,GridUnitType.Star)});main.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});
  var header=new WrapPanel{Margin=new Thickness(0,0,0,15)};AutomationProperties.SetName(picker,"当前项目，范围仅本工作台沙盒");header.Children.Add(picker);
  header.Children.Add(Button("新建项目",()=>EditProject(true),true));
  header.Children.Add(Button(w.Demo?"返回本地模式":"查看独立示例",()=>{var next=new Workspace(w.Root,!w.Demo);next.SeedExamples();var win=new MainWindow(next);win.Show();Close();}));
  header.Children.Add(Button("刷新项目文档",()=>{Refresh();preview="";Show(page);}));
  header.Children.Add(Button("恢复与副本",()=>{ContentPage("恢复与副本");Row(Button("预览ZIP恢复",ChooseRecovery,true),Button("打开恢复副本",ChooseExistingRecovery));Card("范围","仅本工作区runs内的备份与恢复副本，当前没有项目也可访问。");}));
  main.Children.Add(header);
  picker.SelectionChanged+=(_,__)=>{if(picker.SelectedIndex<0||picker.SelectedIndex>=entries.Length)return;selected=entries[picker.SelectedIndex].Id;taskId="";filter="";history=false;preview="";LoadCurrent();Show(page);};
  var scroll=new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};Grid.SetRow(scroll,1);main.Children.Add(scroll);
  notice.FontSize=12;notice.Foreground=Color("#556B87");notice.Margin=new Thickness(0,15,0,0);Grid.SetRow(notice,2);main.Children.Add(notice);
  PreviewKeyDown+=(_,e)=>{if(e.Key==System.Windows.Input.Key.Escape){Show("项目总览");e.Handled=true;}};
  Refresh();Show(page);
 }
 static SolidColorBrush Color(string hex)=>new((System.Windows.Media.Color)ColorConverter.ConvertFromString(hex));
 static TextBlock Text(string s,double size=14,string color="#233B5A",bool bold=false)=>new(){Text=s,FontSize=size,Foreground=Color(color),FontWeight=bold?FontWeights.SemiBold:FontWeights.Normal,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,3,0,9)};
 WButton Button(string label,Action action,bool primary=false,bool enabled=true) {
  var b=new WButton{Content=label,Padding=new Thickness(13,10,13,10),Margin=new Thickness(0,4,10,5),Background=Color(primary?"#3568D4":"#EAF0F8"),Foreground=primary?Brushes.White:Color("#294B79"),BorderThickness=new Thickness(0),IsEnabled=enabled};
  AutomationProperties.SetName(b,label);b.Click+=(_,__)=>{try{action();}catch(Exception ex){notice.Text="操作未完成："+ex.Message+"。原记录未自动覆盖。";}};actions[label]=b;return b;
 }
 void Row(params WButton[] bs){var row=new WrapPanel{Margin=new Thickness(0,5,0,14)};foreach(var b in bs)row.Children.Add(b);body.Children.Add(row);}
 void Card(string title,string text,bool warning=false) {
  var panel=new StackPanel();panel.Children.Add(Text(title,17,"#233B5A",true));panel.Children.Add(Text(text,14,"#54667E"));
  body.Children.Add(new Border{Padding=new Thickness(18),Margin=new Thickness(0,0,0,13),CornerRadius=new CornerRadius(9),Background=Color(warning?"#FFF3DB":"#FFFFFF"),BorderBrush=Color("#DDE5F1"),BorderThickness=new Thickness(1),Child=panel});
 }
 void Refresh(){
  entries=workspace.List();picker.ItemsSource=entries.Select(x=>x.Name+(x.Error!=""?" · 需恢复":"")).ToArray();
  var index=Array.FindIndex(entries,e=>e.Id==selected);if(index<0&&entries.Length>0)index=0;
  picker.SelectedIndex=index;if(index>=0){selected=entries[index].Id;LoadCurrent();}else{project=null;selected="";}
 }
 void LoadCurrent(){try{project=workspace.Load(selected);}catch(Exception ex){project=null;notice.Text="记录读取失败："+ex.Message;}}
 WorkTask? Current=>project?.Tasks.FirstOrDefault(t=>t.Id==taskId)??project?.Tasks.FirstOrDefault();
 void Show(string name) {
  page=name;body.Children.Clear();body.Children.Add(Text(name,28,"#142B4B",true));
  body.Children.Add(Text(workspace.Demo?"独立演示模式 · 虚构内容，仅用于体验":"本地模式 · 数据仅保存于 AI-Project-Workbench 内",12,"#627896"));
  foreach(var label in new[]{"项目总览","任务与接手","知识与冲突","交接与备份","环境与边界"})if(actions.TryGetValue(label,out var b)){b.Background=Color(label==page?"#3568D4":"#20344F");b.Foreground=Brushes.White;}
  if(project==null) {
   Card(selected==""?"创建你的第一个沙盒项目":"记录读取失败",selected==""?"只需要名称与目标。项目拥有独立任务、知识和交接；App 不会读取其他工作区。":"损坏记录不会被示例或空记录覆盖。可先保留当前文件，再从上一版本恢复。",selected!="");
   Row(Button("创建第一个项目",()=>EditProject(true),true));
   if(selected!=""&&workspace.CanRestore(selected))Row(Button("恢复上一版本",Restore));
   return;
  }
  notice.Text="范围："+project.Name+" / "+project.Id+" · 记录 v"+project.Version+" · Esc 返回总览";
  if(project.ExternalChanges.Count>0){Card("外部Markdown修改已读取 · 待本人核对",string.Join("\n",project.ExternalChanges)+"\n本App未把文件编辑视为新的批准；普通保存被阻止。",true);Row(Button("核对外部文档差异",ReviewExternal,true));}
  if(page=="项目总览")Overview();
  if(page=="任务与接手")Tasks();
  if(page=="知识与冲突")Knowledge();
  if(page=="交接与备份")Handoff();
  if(page=="环境与边界")EnvironmentPage();
 }
 void Overview() {
  Card(project!.Name,project.Goal);
  var t=Current;
  Card("下一步",t==null?"尚无任务。创建一个有明确目标的任务即可开始。":t.Goal+"\n"+(t.Next==""?"尚未填写下一步。":t.Next));
  var risks=project.Knowledge.Count(k=>k.Lifecycle=="active"&&(k.ConflictWith!=""||k.Verification!="verified"));
  Card("当前风险",risks+" 条有效知识待核实或有显式冲突。\n"+(t==null?"无验证记录":workspace.Verify(project,t)),risks>0);
  Row(Button("编辑项目",()=>EditProject(false)),Button("新建任务",()=>EditTask(null),true),Button("查看当前任务",()=>Show("任务与接手")));
  Card("项目独立文档",project.Format=="markdown-v3"?"可编辑事实入口："+workspace.MarkdownEntry(project.Id):"旧版独立JSON；尚未生成同源Markdown。");
  Row(Button("检查项目规范",()=>{var issues=workspace.CheckTemplate(project);Card("机械检查结果",issues.Length==0?"目录、文档身份和模板检查通过；不代表功能测试通过。":string.Join("\n",issues),issues.Length>0);}),Button("查看AI入口",()=>Card("AI精简入口与版本",workspace.TemplateText(project))));
  if(project.Format=="legacy-json")Row(Button("预览Markdown升级",()=>{if(MessageBox.Show(this,"将新建同源Markdown快照、AGENTS/WORKFLOW/TESTING和CURRENT入口；旧JSON作为历史保留。只改本项目沙盒。确认？","升级预览",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;workspace.UpgradeMarkdown(project);Refresh();Show(page);},true));
  Row(Button("补齐模板缺项",()=>{var issues=workspace.CheckTemplate(project);if(MessageBox.Show(this,string.Join("\n",issues)+"\n只补缺失模板，不覆盖已有内容。继续？","补齐预览",MessageBoxButton.YesNo)!=MessageBoxResult.Yes)return;workspace.FillMissingTemplate(project);Refresh();Show(page);}));
  if(project.Events.Count>0)Card("最近保存",string.Join("\n",project.Events.TakeLast(3)));
 }
 void Tasks() {
  Row(Button("新建任务",()=>EditTask(null),true));
  if(project!.Tasks.Count==0){Card("任务为空","创建任务后可填写负责人、允许范围、完成情况与下一步。");return;}
  var selector=new ComboBox{ItemsSource=project.Tasks.Select(t=>t.Id+" · "+t.Goal).ToArray(),SelectedIndex=project.Tasks.FindIndex(t=>t.Id==Current!.Id),Margin=new Thickness(0,0,0,14),Padding=new Thickness(8)};
  AutomationProperties.SetName(selector,"当前项目任务");
  selector.SelectionChanged+=(_,__)=>{taskId=project.Tasks[selector.SelectedIndex].Id;preview="";Show(page);};body.Children.Add(selector);
  var t=Current!;Card(t.Goal,"状态："+t.Status+" · 负责人："+t.Owner+"\n允许范围："+t.Scope+"\n下一步："+t.Next+"\n"+workspace.Verify(project,t));
  var observedGit=workspace.ReadGit(project,t);Card("Git只读来源",observedGit.State+" · 分支："+observedGit.Branch+"\n提交："+observedGit.Commit+"\n范围："+observedGit.Scope+"；文件："+observedGit.Files.Count+"\n"+observedGit.Detail);
  Row(Button("编辑任务",()=>EditTask(t)),Button("预览接手与交接",()=>{preview=workspace.Package(project,t);previewAnchor=workspace.Anchor(project,t);Show("交接与备份");},true),Button("关联验证证据",()=>EditEvidence(t)));
  Card("验证边界","记录验证证据仅核对本项目文件存在、SHA256和来源锚点。App不能把“AI说通过”变成真实测试通过。\n状态 done 需至少具备有效文件证据，仍需人为检查测试含义。");
 }
 void Knowledge() {
  Row(Button("新增知识",()=>EditFact(null),true),Button(history?"隐藏历史":"显示历史",()=>{history=!history;Show(page);}));
  var query=new TextBox{Text=filter,Padding=new Thickness(10),MinHeight=40};AutomationProperties.SetName(query,"仅搜索当前项目知识正文与来源");
  body.Children.Add(Text("搜索范围："+project!.Name+" · 当前 project_id；默认排除历史",12,"#627896"));body.Children.Add(query);
  Row(Button("筛选",()=>{filter=query.Text;Show(page);}),Button("清除筛选",()=>{filter="";Show(page);}));
  var list=project.Knowledge.Where(k=>(history||k.Lifecycle=="active")&&(filter==""||(k.Text+" "+k.Source).Contains(filter,StringComparison.OrdinalIgnoreCase))).ToArray();
  if(list.Length==0)Card("没有匹配知识",filter==""?"当前项目尚无知识记录。":"只搜索当前项目；清除筛选可显示全部有效记录。");
  foreach(var k in list) {
   var other=project.Knowledge.FirstOrDefault(x=>x.Id==k.ConflictWith);
   Card(k.Id+" · "+k.Role,k.Text+"\n来源："+k.Source+" · 范围："+k.Scope+" · 修订："+k.Revision+"\n生命周期："+k.Lifecycle+" · 验证："+k.Verification+" · 核实时间："+(k.VerifiedAt==""?"未核实":k.VerifiedAt)+"\n替代："+(k.Supersedes==""?"无":k.Supersedes)+(k.ConflictWith==""?"":"\n显式冲突对象："+k.ConflictWith+"\n对方记录："+(other==null?"缺失；需核对":other.Text+" / 来源："+other.Source)),k.ConflictWith!="");
   Row(Button("编辑 "+k.Id,()=>EditFact(k)));
  }
  Card("判断原则","显式冲突由记录者关联，App并列展示证据；自动语义冲突判断未实现。\n已批准要求未实现仍保持有效；代码变化只触发重核。");
 }
 void Handoff() {
  var t=Current;
  Card("备份与恢复","事实记录备份用于轻量交接。完整内容备份涵盖本项目素材/数据/输入/证据及源码，清单明确缓存、凭据、链接及Git元数据排除项。恢复到新独立副本，原项目保留。");
  Row(Button("预览完整内容备份",ReviewFullBackup,true),Button("预览ZIP恢复",ChooseRecovery),Button("打开恢复副本",ChooseExistingRecovery));
  Row(Button("备份项目记录",()=>{var path=workspace.Backup(project!);notice.Text="已落盘备份："+path+"（记录+上一版本；不含资产/证据）";}),Button("恢复上一版本",Restore,false,workspace.CanRestore(selected)));
  if(t==null){Card("尚无交接任务","先创建一个任务。备份可独立导出当前项目记录。");return;}
  Row(Button("重新生成预览",()=>{preview=workspace.Package(project!,t);previewAnchor=workspace.Anchor(project!,t);Show(page);},true));
  if(preview==""){Card("先预览，再导出","生成材料后检查范围、来源、待核实和冲突。");return;}
  var fresh=workspace.Load(selected);var current=fresh.Tasks.FirstOrDefault(x=>x.Id==t.Id);
  var valid=current!=null&&workspace.Anchor(fresh,current)==previewAnchor&&workspace.Package(fresh,current)==preview;
  int split=preview.IndexOf("\n\n# ",StringComparison.Ordinal);
  Card(valid?"接手与交接预览":"预览已过期 · 需重建",split>=0?preview[(split+2)..]:preview,!valid);
  if(split>=0)body.Children.Add(new Expander{Header="来源版本与SHA256 · 展开核对",Content=Text(preview[..split],12),Margin=new Thickness(0,0,0,14)});
  Row(Button("复制预览",()=>{if(!valid)throw new InvalidOperationException("预览已过期");System.Windows.Clipboard.SetText(preview);notice.Text="已复制当前项目预览。";},false,valid),
   Button("导出 Markdown",()=>{var path=workspace.Export(project!,t,preview,previewAnchor);notice.Text="已落盘："+path;},true,valid));
 }
 void EnvironmentPage(){
  Card("当前依赖","原生 WPF / .NET 8。当前进程版本："+System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription+"\n复用本机 Microsoft 运行时，未新增第三方包。");
  var files=workspace.List();long recordBytes=files.Where(x=>x.Error=="").Sum(x=>workspace.FactStorageBytes(x.Id));Card("本项目存储预览","本模式沙盒项目："+files.Length+" 个。事实文档与必要历史占用："+recordBytes+" 字节（已登记范围）。\n唯一数据根："+Path.Combine(workspace.Root,"data",workspace.Mode,"Projects")+"\n统计仅登记记录，不盘点其他项目。备份输出在本项目 runs/exports。");
  Card("尚未开放的能力","现有工作区导入、真实 AI 连接、受控安装执行、清理/删除、系统设置、跨电脑同步与RAG均未开放。\n未来安装计划必须记录来源、版本、目录、空间、授权和证据；清理只按精确候选预览。");
 }
 void OpenEditor(string title,(string,string,string,string[]?)[] schema,Func<Dictionary<string,string>,ProjectRecord> change) {
  var baseline=title=="新建项目"||project==null?null:Workspace.Clone(project);
  var editor=new EditorWindow(title,schema,values=>{
   var next=change(values);
   if(baseline!=null)workspace.Save(next,baseline.Version,title);
   selected=next.Id;preview="";return true;
  }){Owner=this};
  editor.ShowDialog();Refresh();Show(page);
 }
 void EditProject(bool create){
  var original=project==null?null:Workspace.Clone(project);
  OpenEditor(create?"新建项目":"编辑项目",new[]{("name","名称 *",create?"":project!.Name,(string[]?)null),("goal","项目目标 *",create?"":project!.Goal,null)},v=>{
   if(create)return workspace.Create(v["name"],v["goal"]);
   var p=Workspace.Clone(original!);p.Name=v["name"];p.Goal=v["goal"];return p;
  });
 }
 void EditTask(WorkTask? existing) {
  var original=Workspace.Clone(project!);var item=existing==null?new WorkTask():original.Tasks.First(t=>t.Id==existing.Id);
  OpenEditor(existing==null?"新建任务":"编辑任务",new[]{
   ("goal","任务目标 *",item.Goal,(string[]?)null),("owner","负责人",item.Owner,null),("status","状态",item.Status,new[]{"planned","running","blocked","review","cleanup_pending","done"}),
   ("scope","允许编辑范围（项目内相对说明）",item.Scope,null),("related","需要核对的相关文件（项目内相对路径，每行一个）",string.Join("\n",item.RelatedFiles),null),("done","已做（写明证据）",item.Done,null),("notdone","未做 / 阻碍",item.NotDone,null),("next","下一步",item.Next,null)
  },v=>{
   var p=Workspace.Clone(original);var t=existing==null?new WorkTask{Id=item.Id}:p.Tasks.First(x=>x.Id==existing.Id);
   t.Goal=v["goal"];t.Owner=v["owner"];t.Status=v["status"];t.Scope=v["scope"];t.RelatedFiles=v["related"].Split('\n',StringSplitOptions.RemoveEmptyEntries).Select(x=>workspace.NormalizeRelated(x)).Distinct().ToList();t.Done=v["done"];t.NotDone=v["notdone"];t.Next=v["next"];
   if(t.Status=="done"&&!workspace.Verify(p,t).StartsWith("verified"))throw new InvalidOperationException("缺少与当前来源一致的证据；可保存为 review。");
   if(existing==null)p.Tasks.Add(t);taskId=t.Id;return p;
  });
 }
 void EditFact(Fact? existing) {
  var original=Workspace.Clone(project!);var item=existing==null?new Fact():original.Knowledge.First(k=>k.Id==existing.Id);
  OpenEditor(existing==null?"新增知识":"编辑知识（修改批准要求须本人确认）",new[]{
   ("text","知识正文 *",item.Text,(string[]?)null),("source","来源引用 / 用户确认记录 *",item.Source,null),("scope","适用范围",item.Scope,null),
   ("role","来源角色",item.Role,new[]{"已批准意图","观察实现","待批准方案","复用经验"}),("lifecycle","生命周期（独立于验证）",item.Lifecycle,new[]{"active","superseded","retired"}),
   ("verification","核实状态（本人记录）",item.Verification,new[]{"unknown","needs_verification","verified"}),("revision","来源修订",item.Revision,null),("supersedes","替代的知识ID（可空）",item.Supersedes,null),("conflict","显式冲突对象ID（可空）",item.ConflictWith,null)
  },v=>{
   var p=Workspace.Clone(original);var k=existing==null?new Fact{Id=item.Id}:p.Knowledge.First(x=>x.Id==existing.Id);
   k.Text=v["text"];k.Source=v["source"];k.Scope=v["scope"];k.Role=v["role"];k.Lifecycle=v["lifecycle"];k.Verification=v["verification"];k.Revision=v["revision"];k.Supersedes=v["supersedes"];k.ConflictWith=v["conflict"];
   if(k.ConflictWith!=""&&!p.Knowledge.Any(x=>x.Id==k.ConflictWith&&x.Id!=k.Id))throw new InvalidOperationException("冲突对象必须是当前项目另一条知识ID。");
   if(k.Supersedes!=""&&!p.Knowledge.Any(x=>x.Id==k.Supersedes&&x.Id!=k.Id))throw new InvalidOperationException("替代对象必须是当前项目另一条知识ID。");
   if(existing!=null&&existing.Role=="已批准意图"&&existing.Text!=k.Text&&MessageBox.Show(this,"这将修改已批准意图。只有你确认的新要求才能保存；否则请另建待批准方案。","确认要求修订",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)throw new InvalidOperationException("未确认要求修订，输入已保留。");
   Workspace.FinishFactEdit(existing,k);
   if(existing==null)p.Knowledge.Add(k);return p;
  });
 }
 void EditEvidence(WorkTask item){
  var original=Workspace.Clone(project!);
  OpenEditor("关联验证证据",new[]{("path","当前项目 runs 内的相对文件路径 *",item.Evidence,(string[]?)null)},v=>{
   var p=Workspace.Clone(original);var t=p.Tasks.First(x=>x.Id==item.Id);workspace.RecordEvidence(p,t,v["path"]);return p;
  });
 }
 void ReviewExternal(){
  body.Children.Clear();body.Children.Add(Text("核对外部文档差异",28,"#142B4B",true));
  Row(Button("返回项目总览",()=>Show("项目总览")));
  Card("原基线与当前文档（均属于本项目）",workspace.ExternalDiff(project!),true);
  Row(Button("本人确认并保存新基线",()=>{if(MessageBox.Show(this,"确认这些外部变化真实符合你的批准意图？该动作不会改写要求来适配代码，相关知识仍标待复核。未确认请取消。","本人核对",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;workspace.AcceptExternal(project!);Refresh();Show(page);},true));
 }
 void Restore(){
  if(MessageBox.Show(this,"恢复上一版本？当前文件将保留到该项目 archive，恢复不会删除任何项目。","确认恢复",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
  workspace.Restore(selected);preview="";Refresh();Show(page);
 }
 public void Invoke(string label)=>actions[label].RaiseEvent(new RoutedEventArgs(WButton.ClickEvent));
 public void Select(int index)=>picker.SelectedIndex=index;
 public string Visible()=>string.Join("\n",Flatten(body).OfType<TextBlock>().Select(t=>t.Text));
 static IEnumerable<DependencyObject> Flatten(DependencyObject obj){yield return obj;for(int i=0;i<VisualTreeHelper.GetChildrenCount(obj);i++)foreach(var c in Flatten(VisualTreeHelper.GetChild(obj,i)))yield return c;}
 public void Render(string path,double scale=1){
  UpdateLayout();var element=(FrameworkElement)Content;var bitmap=new RenderTargetBitmap((int)(element.ActualWidth*scale),(int)(element.ActualHeight*scale),96*scale,96*scale,PixelFormats.Pbgra32);bitmap.Render(element);
  var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(path);png.Save(output);
 }
}
public static class Program {
 [STAThread] public static int Main(string[] args){
  var root=FindRoot();
  bool recoveryDemo=false;
  int workspaceArgument=Array.IndexOf(args,"--workspace");
  if(workspaceArgument>=0){if(workspaceArgument+1>=args.Length)throw new InvalidOperationException("--workspace缺少恢复副本路径");var restored=RecoveryStartup.Resolve(root,args[workspaceArgument+1]);root=restored.Root;recoveryDemo=restored.Demo;}
  if(args.Contains("--self-test"))return Tests.Run(root);
  var runtimeRoot=root;
  if(args.Contains("--ui-smoke")){runtimeRoot=Path.Combine(root,"runs","v4-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(runtimeRoot);File.Copy(Path.Combine(root,"project.json"),Path.Combine(runtimeRoot,"project.json"));Directory.CreateDirectory(Path.Combine(runtimeRoot,"runs"));}
  var w=new Workspace(runtimeRoot,recoveryDemo||args.Contains("--demo")||args.Contains("--ui-smoke"));w.SeedExamples();
  var app=new Application();var win=new MainWindow(w);
  if(args.Contains("--ui-smoke"))win.Loaded+=async(_,__)=>{
   var lines=new List<string>();
   try{
        var local=new Workspace(runtimeRoot);var empty=new MainWindow(local);empty.Show();
    await System.Threading.Tasks.Task.Delay(150);Tests.Check(local.List().Length==0&&empty.Visible().Contains("创建你的第一个"),"WPF default empty local",lines);
    empty.Render(Path.Combine(root,"runs","mvp-empty.png"));
    empty.Invoke("恢复与副本");Tests.Check(empty.Visible().Contains("当前没有项目也可访问"),"WPF recovery accessible from empty local state",lines);
    empty.Invoke("打开恢复副本");Tests.Check(empty.Visible().Contains("尚无恢复副本"),"WPF recovery inventory empty state",lines);empty.Close();
    await System.Threading.Tasks.Task.Delay(300);win.Render(Path.Combine(root,"runs","mvp-overview.png"));
    win.Invoke("任务与接手");await System.Threading.Tasks.Task.Delay(100);
    win.Invoke("预览接手与交接");await System.Threading.Tasks.Task.Delay(100);
    Tests.Check(win.Visible().Contains("待核实与待批准"),"WPF package preview",lines);
    win.Render(Path.Combine(root,"runs","mvp-handoff.png"));
    win.Invoke("知识与冲突");await System.Threading.Tasks.Task.Delay(100);
    Tests.Check(win.Visible().Contains("显式冲突对象")&&!win.Visible().Contains("旧版仅 CSV"),"WPF conflict/history separation",lines);
    win.Select(1);Tests.Check(win.Visible().Contains("轨道清单")&&!win.Visible().Contains("星图笔记"),"WPF project scope",lines);
    win.Render(Path.Combine(root,"runs","mvp-knowledge.png"));
    var uiProject=w.List().First(x=>x.Name.Contains("轨道")).Id;
    var uiSource=w.SnapshotFile(uiProject,"PROJECT.md");
    File.WriteAllText(uiSource,File.ReadAllText(uiSource).Replace("验证本项目内的知识与任务闭环","UI夹具外部编辑目标"));
    win.Invoke("刷新项目文档");win.Invoke("项目总览");
    Tests.Check(win.Visible().Contains("UI夹具外部编辑目标")&&win.Visible().Contains("外部"),"WPF refresh external Markdown pending review",lines);
    win.Invoke("核对外部文档差异");win.Render(Path.Combine(root,"runs","mvp-source-conflict.png"));
    win.Invoke("项目总览");foreach(var scale in new[]{1.25,1.5,2.0})win.Render(Path.Combine(root,"runs","mvp-scale-"+scale.ToString(System.Globalization.CultureInfo.InvariantCulture)+".png"),scale);
        win.Width=900;win.Height=620;await System.Threading.Tasks.Task.Delay(100);win.Render(Path.Combine(root,"runs","mvp-compact.png"));
    var gitFixture=GitAssetTests.Fixture(w);var gitWindow=new MainWindow(w);gitWindow.Show();
    gitWindow.Select(Array.FindIndex(w.List(),x=>x.Id==gitFixture.Id));gitWindow.Invoke("任务与接手");
    Tests.Check(gitWindow.Visible().Contains("分支：fixture")&&gitWindow.Visible().Contains("Git只读来源"),"WPF real Git source summary",lines);
    gitWindow.Render(Path.Combine(root,"runs","mvp-git.png"));
    gitWindow.Invoke("交接与备份");gitWindow.Invoke("预览完整内容备份");
    Tests.Check(gitWindow.Visible().Contains("本项目内容清单")&&gitWindow.Visible().Contains("repo/.git"),"WPF full content include exclusion preview",lines);
    gitWindow.Render(Path.Combine(root,"runs","mvp-backup-preview.png"));
    gitWindow.Invoke("确认创建内容备份");
    Tests.Check(gitWindow.Visible().Contains("备份已落盘并校验"),"WPF backup action actual file IO",lines);
    var uiZip=Directory.GetFiles(w.Safe("runs/exports"),"project-content-"+gitFixture.Id+"-*.zip").Single();
    gitWindow.Invoke("预览ZIP恢复");gitWindow.Invoke("预览选中ZIP");
    Tests.Check(gitWindow.Visible().Contains("新目标：")&&gitWindow.Visible().Contains("不覆盖原项目"),"WPF independent restore confirmation preview",lines);
    gitWindow.Render(Path.Combine(root,"runs","mvp-restore-preview.png"));
    gitWindow.Invoke("返回交接与备份");Tests.Check(gitWindow.Visible().Contains("备份与恢复"),"WPF content preview return",lines);gitWindow.Close();
    lines.Add("NOT RUN: external keyboard/mouse and physical DPI switching; supported native CU tool absent; current desktop unlock not verified (previously locked)");
    lines.Add("RENDER ONLY: 125/150/200% render resolution; not actual OS scaling");
    File.WriteAllLines(Path.Combine(root,"runs","mvp-ui.log"),lines);
   }catch(Exception ex){File.WriteAllText(Path.Combine(root,"runs","mvp-ui.log"),"FAIL: "+ex);Environment.ExitCode=1;}
   win.Close();
  };
  app.Run(win);return Environment.ExitCode;
 }
 static string FindRoot(){var d=new DirectoryInfo(AppContext.BaseDirectory);while(d!=null){if(File.Exists(Path.Combine(d.FullName,"project.json")))return d.FullName;d=d.Parent;}throw new InvalidOperationException("请从完整独立项目目录运行。");}
}
public static class Tests {
 public static void Check(bool yes,string name,List<string> logs){if(!yes)throw new Exception(name);logs.Add("PASS: "+name);}
 public static int Run(string root){
  var logs=new List<string>();var folder=Path.Combine(root,"runs","mvp-test-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(folder);File.Copy(Path.Combine(root,"project.json"),Path.Combine(folder,"project.json"));
  try{
   var w=new Workspace(folder);Check(w.List().Length==0,"default local empty",logs);
   var p=w.Create("测试项目","内部夹具目标");var q=w.Create("隔离项目","另一内部目标");
   var t=new WorkTask{Goal="CSV/JSON验证",Next="核对证据"};p.Tasks.Add(t);
   p.Knowledge.Add(new Fact{Id="K-1",Text="支持CSV与JSON",Role="已批准意图",Source="夹具批准R2"});
   p.Knowledge.Add(new Fact{Id="K-2",Text="当前仅CSV",Role="观察实现",Source="夹具代码",ConflictWith="K-1"});
   p.Knowledge.Add(new Fact{Id="K-3",Text="旧版仅CSV",Role="已批准意图",Source="夹具R1",Lifecycle="superseded",Verification="verified"});
   w.Save(p,p.Version,"fixture");
   Check(new Workspace(folder).Load(p.Id).Tasks[0].Goal==t.Goal,"restart persistence",logs);
   Check(w.Load(q.Id).Knowledge.Count==0,"project records isolated",logs);
   var package=w.Package(p,t);Check(!package.Contains("旧版仅CSV")&&package.Contains("显式冲突"),"history excluded conflict retained",logs);
   Check(w.Verify(p,t).StartsWith("unknown"),"missing evidence unknown",logs);
   var export1=w.Export(p,t,package,w.Anchor(p,t));var export2=w.Export(p,t,package,w.Anchor(p,t));
   Check(export1!=export2&&File.Exists(export1)&&File.Exists(export2),"repeated export no overwrite",logs);
   var a=w.Load(p.Id);var b=w.Load(p.Id);a.Goal="改变项目目标";w.Save(a,a.Version,"changed");
   try{w.Save(b,b.Version,"stale");throw new Exception("stale accepted");}catch(InvalidOperationException){logs.Add("PASS: stale save rejected");}
   try{w.Export(p,t,package,w.Anchor(p,t));throw new Exception("stale exported");}catch(InvalidOperationException){logs.Add("PASS: stale preview rejected");}
   var evidence=Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(w.RecordPath(p.Id))))!,"runs","test.txt");
   // Build exact fixture path through the guarded public root.
   evidence=w.Safe("data/local/Projects/"+p.Id+"/runs/test.txt");File.WriteAllText(evidence,"fixture test result; not integration");
   var current=w.Load(p.Id);var task=current.Tasks[0];w.RecordEvidence(current,task,"runs/test.txt");w.Save(current,current.Version,"evidence");
   Check(w.Verify(current,task).StartsWith("verified"),"evidence file association",logs);
   File.AppendAllText(evidence,"changed");Check(w.Verify(current,task).StartsWith("needs_verification"),"changed evidence needs recheck",logs);
   Check(current.Knowledge[0].Lifecycle=="active","new intent remains active",logs);
   try{w.Safe("../escape");throw new Exception("escape allowed");}catch(InvalidOperationException){logs.Add("PASS: traversal blocked");}
   var backup=w.Backup(current);using(var zip=ZipFile.OpenRead(backup))Check(zip.GetEntry("repo/docs/CURRENT.md")!=null&&zip.GetEntry("manifest.json")!=null,"backup canonical records manifest",logs);
   var editor=new EditorWindow("test",new[]{("goal","目标","before",(string[]?)null)},_=>throw new IOException("fixture write failure"));
   editor.TestSet("goal","unsaved");
   Check(editor.Dirty&&!editor.MayLeave(LeaveDecision.Stay),"dirty stay protection",logs);
   Check(!editor.MayLeave(LeaveDecision.Save)&&editor.TestRead("goal")=="unsaved"&&editor.Error.Contains("输入已保留"),"save failure preserves input",logs);
   Check(editor.MayLeave(LeaveDecision.Discard),"explicit discard permitted",logs);
   File.WriteAllText(w.RecordPath(p.Id),"{corrupt");
   Check(w.List().First(e=>e.Id==p.Id).Error!="","corrupt load explicit",logs);
   var recovered=w.Restore(p.Id);Check(recovered.Id==p.Id&&Directory.GetFiles(w.Safe("data/local/Projects/"+p.Id+"/archive")).Length>=1,"recovery retains corrupt original",logs);
      var qt=new WorkTask{Goal="导出失败夹具"};q.Tasks.Add(qt);w.Save(q,q.Version,"export failure fixture");
   var blocker=w.Safe("data/local/Projects/"+q.Id+"/runs/exports");File.WriteAllText(blocker,"fixture blocks export directory");
   try{w.Export(q,qt,w.Package(q,qt),w.Anchor(q,qt));throw new Exception("failed export claimed success");}catch(IOException){logs.Add("PASS: export failure remains failure");}
   var before=w.List().Length;int commits=0;
   var once=new EditorWindow("duplicate",new[]{("name","名称","",(string[]?)null)},_=>{w.Create("重复提交夹具","记录仅创建一次");commits++;return true;});
   once.TestSet("name","ready");once.TrySave();once.TrySave();Check(commits==1&&w.List().Length==before+1,"duplicate form save only one persistent record",logs);
      var oldFact=new Fact{Text="old",Source="approved",Verification="verified",VerifiedAt="2026-09-01"};
   var lifecycleOnly=new Fact{Text="old",Source="approved",Lifecycle="superseded",Verification="verified"};Workspace.FinishFactEdit(oldFact,lifecycleOnly);
   Check(lifecycleOnly.VerifiedAt=="2026-09-01"&&lifecycleOnly.Verification=="verified","lifecycle change preserves last verification",logs);
   var changedFact=new Fact{Text="new",Source="approved",Verification="verified"};Workspace.FinishFactEdit(oldFact,changedFact);
   Check(changedFact.Verification=="needs_verification"&&changedFact.VerifiedAt=="2026-09-01","content change needs recheck retains history",logs);
   var demo=new Workspace(folder,true);demo.SeedExamples();Check(demo.List().Length==2&&w.List().Length==3,"demo/local store separation",logs);
   var clone=Workspace.Clone(recovered);using(var locked=new FileStream(w.RecordPath(p.Id),FileMode.Open,FileAccess.ReadWrite,FileShare.None)){
    try{w.Save(clone,clone.Version,"locked");throw new Exception("locked save accepted");}catch(IOException){logs.Add("PASS: locked storage rejects write");}
   }
   MarkdownTests.Run(w,logs);
   GitAssetTests.Run(w,logs);
   File.WriteAllLines(Path.Combine(root,"runs","mvp-tests.log"),logs);return 0;
  }catch(Exception ex){File.WriteAllLines(Path.Combine(root,"runs","mvp-tests.log"),logs.Concat(new[]{"FAIL: "+ex}));return 1;}
 }
}
