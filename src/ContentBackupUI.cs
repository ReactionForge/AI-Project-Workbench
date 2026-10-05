using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace Workbench;
public partial class MainWindow {
 void ContentPage(string title){
  body.Children.Clear();body.Children.Add(Text(title,28,"#142B4B",true));
  Row(Button("返回交接与备份",()=>Show("交接与备份")));
 }
 void ContentDetails(ContentBackup plan){
  Card("本项目内容清单",workspace.DescribeContent(plan));
  body.Children.Add(new Expander{Header="包含文件 · 展开检查",Content=Text(string.Join("\n",plan.Files.Select(f=>f.Path+" · "+f.Bytes+" 字节 · "+f.Sha256)),12),Margin=new Thickness(0,0,0,12)});
  if(plan.GitAtBackup.Count>0)body.Children.Add(new Expander{Header="备份时Git来源摘要 · 未包含Git对象历史",Content=Text(string.Join("\n\n",plan.GitAtBackup.Select(g=>g.Key+"\n"+g.Value.Describe())),12),Margin=new Thickness(0,0,0,12)});
  if(plan.Excluded.Count>0)Card("排除清单 · 不跟随链接，不写入凭据",string.Join("\n",plan.Excluded.Select(e=>e.Path+"："+e.Reason)),true);
  if(plan.ReferenceIssues.Count>0)Card("登记引用需核对 · 不自动改写来源",string.Join("\n",plan.ReferenceIssues),true);
 }
 void ReviewFullBackup(){
  var selectedProject=Workspace.Clone(project!);var plan=workspace.PreviewFullBackup(selectedProject);
  ContentPage("预览完整项目内容备份");ContentDetails(plan);
  Card("执行边界","ZIP只写本工作区runs/exports。不会安装、清理、迁移或读取其他项目。未登记的外部引用不会自动抓取；敏感识别为保守规则，不能保证发现所有隐蔽凭据。");
  Row(Button("确认创建内容备份",()=>{
   var path=workspace.CreateFullBackup(selectedProject,plan);
   Card("备份已落盘并校验",path);
   notice.Text="本项目备份完成；排除项以清单为准。";
  },true));
 }
 string ScopedInventoryFolder(string relative){
  var folder=workspace.Safe(relative);if(Directory.Exists(folder)&&(File.GetAttributes(folder)&FileAttributes.ReparsePoint)!=0)throw new IOException("拒绝浏览链接目录");return folder;
 }
 void ChooseRecovery(){
  ContentPage("选择本工作区内容备份");var folder=ScopedInventoryFolder("runs/exports");
  var paths=Directory.Exists(folder)?Directory.GetFiles(folder,"project-content-P-*.zip").OrderByDescending(p=>p,StringComparer.Ordinal).ToArray():Array.Empty<string>();
  Card("选择范围",folder+"\\n仅列本工作区创建的内容ZIP，不浏览其他项目。");
  if(paths.Length==0){Card("尚无内容备份","先预览并创建当前项目内容备份。");return;}
  var picker=new ComboBox{ItemsSource=paths.Select(Path.GetFileName).ToArray(),SelectedIndex=0,Margin=new Thickness(0,0,0,12),Padding=new Thickness(8)};body.Children.Add(picker);
  Row(Button("预览选中ZIP",()=>ReviewRecovery(paths[picker.SelectedIndex]),true));
 }
 void ChooseExistingRecovery(){
  ContentPage("打开已恢复副本");var folder=ScopedInventoryFolder("runs/restored");
  var paths=Directory.Exists(folder)?Directory.GetDirectories(folder).Where(p=>System.Text.RegularExpressions.Regex.IsMatch(Path.GetFileName(p),"^restore-[a-f0-9]{32}$")).OrderByDescending(p=>p,StringComparer.Ordinal).ToArray():Array.Empty<string>();
  Card("范围",folder+"\\n只打开已恢复的独立副本，原项目数据不迁移。");
  if(paths.Length==0){Card("尚无恢复副本","先完成内容备份预览和显式恢复。");return;}
  var picker=new ComboBox{ItemsSource=paths.Select(Path.GetFileName).ToArray(),SelectedIndex=0,Margin=new Thickness(0,0,0,12),Padding=new Thickness(8)};body.Children.Add(picker);
  Row(Button("打开选中副本",()=>{var recovered=RecoveryStartup.Resolve(workspace.Root,paths[picker.SelectedIndex]);new MainWindow(recovered).Show();notice.Text="已打开："+recovered.Root;},true));
 }
 void ReviewRecovery(string path){
  var plan=workspace.PreviewContentRestore(path);ContentPage("预览恢复为独立副本");ContentDetails(plan.Content);
  Card("恢复目标与原件保留","来源项目："+plan.Content.ProjectId+" · "+(plan.Content.Demo?"虚构示例":"本地内容")+"\nZIP SHA256："+plan.ArchiveSha256+"\n新目标："+plan.Destination+"\n恢复新副本不覆盖原项目。Git元数据/缓存/凭据不在备份中，不会自动恢复Git提交状态。");
  Row(Button("确认恢复到新副本",()=>{
   if(MessageBox.Show(this,"已核对目标、包含文件和排除项？恢复将创建新的独立副本，原项目及ZIP保留。是否继续？","确认内容恢复",MessageBoxButton.YesNo,MessageBoxImage.Question)!=MessageBoxResult.Yes)return;
   var root=workspace.RestoreContentCopy(plan,true);
   Card("新副本已恢复并校验",root);
   var window=new MainWindow(new Workspace(root,plan.Content.Demo));window.Show();
  },true));
 }
 public void TestContentBackupPreview()=>ReviewFullBackup();
 public void TestRecoveryPreview(string path)=>ReviewRecovery(path);
}
