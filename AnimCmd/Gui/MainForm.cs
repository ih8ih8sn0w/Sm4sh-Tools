﻿using Sm4shCommand.GUI;
using Sm4shCommand.Nodes;
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Windows.Forms;
using static Sm4shCommand.Runtime;
using SALT.PARAMS;
using SALT.Scripting;
using SALT.Scripting.AnimCMD;
using System.ComponentModel;
using SALT.Scripting.MSC;

namespace Sm4shCommand
{
    public partial class AcmdMain : Form
    {
        public AcmdMain()
        {
            InitializeComponent();
            Manager = new WorkspaceManager();
            ScriptFiles = new SortedList<string, IScriptCollection>();
        }

        public const string FileFilter =
                              "All Supported Files (*.bin, *.mscsb)|*.bin;*.mscsb|" +
                              "ACMD Binary (*.bin)|*.bin|" +
                              "MotionScript Binary (*.mscsb)|*.mscsb|" +
                              "All Files (*.*)|*.*";

        private OpenFileDialog ofDlg = new OpenFileDialog() { Filter = FileFilter };
        private SaveFileDialog sfDlg = new SaveFileDialog() { Filter = FileFilter };
        private FolderSelectDialog fsDlg = new FolderSelectDialog();

        private WorkspaceManager Manager { get; set; }
        public Project ActiveProject
        {
            get
            {
                if (FileTree.SelectedNode == null)
                    return null;

                var node = FileTree.SelectedNode;
                while (!((node = node.Parent) is Project))
                    node = node.Parent;

                if (node is Project)
                    return (Project)node;
                else
                    return null;
            }
        }

        public SortedList<string, IScriptCollection> ScriptFiles { get; set; }
        public MTable MotionTable { get; set; }
        public IDE_MODE IDEMode { get; private set; }

        private void ACMDMain_Load(object sender, EventArgs e)
        {
            Runtime.LogMessage("Checking for external event dictionary..");
            if (File.Exists(Path.Combine(Application.StartupPath, "Events.cfg")))
            {
                Runtime.LogMessage("Event dictionary found, applying overrides..");
                Runtime.LogMessage("============================================");
                Action<object, DoWorkEventArgs> work = (object snd, DoWorkEventArgs arg) =>
                {
                    GetCommandInfo(Path.Combine(Application.StartupPath, "Events.cfg"));
                };
                Action<object, RunWorkerCompletedEventArgs> workDone = (object snd, RunWorkerCompletedEventArgs arg) =>
                {
                    Runtime.LogMessage("============================================\n");
                    Runtime.LogMessage("Done.");
                };
                using (BackgroundWorker wrk = new BackgroundWorker())
                {
                    wrk.DoWork += new DoWorkEventHandler(work);
                    wrk.RunWorkerCompleted += new RunWorkerCompletedEventHandler(workDone);
                    wrk.RunWorkerAsync();
                }
            }

        }
        private void tabControl1_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index > tabControl1.TabPages.Count - 1)
                return;

            if (e.Index == tabControl1.SelectedIndex)
                e.Graphics.FillRectangle(Brushes.ForestGreen, e.Bounds);
            else
                e.Graphics.FillRectangle(SystemBrushes.ActiveBorder, e.Bounds);

            e.Graphics.FillEllipse(new SolidBrush(Color.IndianRed), e.Bounds.Right - 18, e.Bounds.Top + 3, e.Graphics.MeasureString("x", Font).Width + 4, Font.Height);
            e.Graphics.DrawEllipse(Pens.Black, e.Bounds.Right - 18, e.Bounds.Top + 3, e.Graphics.MeasureString("X", Font).Width + 3, Font.Height);
            e.Graphics.DrawString("X", new Font(e.Font, FontStyle.Bold), Brushes.Black, e.Bounds.Right - 17, e.Bounds.Top + 3);
            e.Graphics.DrawString(tabControl1.TabPages[e.Index].Text, e.Font, Brushes.Black, e.Bounds.Left + 17, e.Bounds.Top + 3);
            e.DrawFocusRectangle();
        }

        private void cboMode_SelectedIndexChanged(object sender, EventArgs e)
        {
            Runtime.WorkingEndian = (Endianness)cboMode.SelectedIndex;
        }
        private void aboutToolStripMenuItem_Click_1(object sender, EventArgs e)
        {
            var abtBox = new AboutBox();
            abtBox.ShowDialog();
        }

        private unsafe void fOpen_Click(object sender, EventArgs e)
        {
            if (ofDlg.ShowDialog() == DialogResult.OK)
            {
                FileTree.Nodes.Clear(); ScriptFiles.Clear();
                if (ofDlg.FileName.EndsWith(".bin"))
                {
                    DataSource source = new DataSource(FileMap.FromFile(ofDlg.FileName));
                    if (*(buint*)source.Address == 0x41434D44) // ACMD
                    {
                        if (*(byte*)(source.Address + 0x04) == 0x02)
                            Runtime.WorkingEndian = Endianness.Little;
                        else if ((*(byte*)(source.Address + 0x04) == 0x00))
                            Runtime.WorkingEndian = Endianness.Big;

                        var f = new ACMDFile(source);
                        ScriptFiles.Add(ofDlg.FileName, f);
                        var node = new TreeNode("ACMD") { Name = "nACMD" };
                        foreach (var keypair in f.Scripts)
                            node.Nodes.Add(new ScriptNode(keypair.Key, $"{keypair.Key:X8}", keypair.Value));
                        FileTree.Nodes.Add(node);
                    }
                }
                else if (ofDlg.FileName.EndsWith(".mscsb")) // MSC
                {
                    var f = new MSCFile(ofDlg.FileName);
                    ScriptFiles.Add(ofDlg.FileName, f);

                    var node = new TreeNode("MSC") { Name = "nMSC" };
                    for (int i = 0; i < f.Scripts.Count; i++)
                        node.Nodes.Add(new ScriptNode((uint)i, $"{i:X8}", f.Scripts.Values[i]));
                    FileTree.Nodes.Add(node);
                }
                IDEMode = IDE_MODE.File;
            }

        }
        private void fitOpen_Click(object sender, EventArgs e)
        {
            if (fsDlg.ShowDialog() == DialogResult.OK)
            {
                FileTree.Nodes.Clear(); ScriptFiles.Clear();
                foreach (var p in Directory.EnumerateFiles(fsDlg.SelectedPath))
                {
                    if (p.EndsWith(".bin"))
                    {
                        var acmd = new ACMDFile(p);
                        ScriptFiles.Add(p, acmd);
                        Runtime.WorkingEndian = acmd.Endian;
                    }
                    else if (p.EndsWith(".mtable"))
                        MotionTable = new MTable(p, Runtime.WorkingEndian);
                }
                var acmdnode = new TreeNode("ACMD") { Name = "nACMD" };

                for (int i = 0; i < MotionTable.Count; i++)
                {
                    var node = new ScriptNode(MotionTable[i], $"{i:X} - [{MotionTable[i]:X8}]");
                    foreach (var keypair in ScriptFiles)
                    {
                        if (keypair.Value.Scripts.Keys.Contains(MotionTable[i]))
                            node.Scripts.Add(keypair.Key, keypair.Value.Scripts[MotionTable[i]]);
                    }
                    acmdnode.Nodes.Add(node);
                }
                FileTree.Nodes.Add(acmdnode);
                IDEMode = IDE_MODE.Fighter;
            }
        }
        private void projOpen_Click(object sender, EventArgs e)
        {
            ProjectWizard dlg = new ProjectWizard();
            dlg.ShowDialog();
            if (dlg.DialogResult == DialogResult.OK)
            {
                Manager.OpenProject(dlg.Project.ProjPath);
                IDEMode = IDE_MODE.Project;
            }
        }
        private void saveToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IDEMode == IDE_MODE.Project)
            {
                if (Manager.Projects.Count > 0)
                    foreach (var proj in Manager.Projects.Values)
                        proj.Save();
            }
            else if (IDEMode == IDE_MODE.Fighter)
            {
                foreach (var keypair in ScriptFiles)
                {
                    keypair.Value.Export(Path.GetFileName(keypair.Key));
                }
                MotionTable.Export(
                    Path.Combine(Path.GetDirectoryName(ScriptFiles.Keys[0]), "motion.mtable"));
            }
            else if (IDEMode == IDE_MODE.File)
            {
                ScriptFiles.Values[0].Export(ScriptFiles.Keys[0]);
            }
        }
        private void saveAsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (IDEMode == IDE_MODE.Project)
            {
                if (fsDlg.ShowDialog() == DialogResult.OK)
                {
                    throw new NotImplementedException("Saving workspaces not yet supported");

                    //if (Manager.Projects.Count > 0)
                    //    foreach (var proj in Manager.Projects.Values)
                    //        proj.Save();
                }
            }
            else if (IDEMode == IDE_MODE.Fighter)
            {
                if (fsDlg.ShowDialog() == DialogResult.OK)
                {
                    foreach (var keypair in ScriptFiles)
                    {
                        keypair.Value.Export(Path.Combine(fsDlg.SelectedPath, keypair.Key));
                    }
                    MotionTable.Export(Path.Combine(fsDlg.SelectedPath, "motion.mtable"));
                }
            }
            else if (IDEMode == IDE_MODE.File)
            {
                sfDlg.FileName = Path.GetFileNameWithoutExtension(ScriptFiles.Keys[0]);
                if (sfDlg.ShowDialog() == DialogResult.OK)
                    ScriptFiles.Values[0].Export(sfDlg.FileName);

            }
        }

        private void ProjectToolStripMenuItem_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Filter = "All Files (*.*)|*.*|Project (*.fitproj)|*.fitproj";
                if (dlg.ShowDialog() == DialogResult.OK)
                    Manager.OpenProject(dlg.FileName);
            }
        }

        private void FileTree_NodeMouseDoubleClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            TabPage p = null;
            if (e.Node is ScriptNode)
            {
                TreeNode root = e.Node.Parent;
                while (root.Parent != null)
                    root = root.Parent;

                if (root?.Name == "nACMD")
                    p = new CodeEditor(e.Node as ScriptNode, ACMD_INFO.CMD_NAMES.Values.ToArray());
                else if (root?.Name == "nMSC")
                    p = new CodeEditor(e.Node as ScriptNode, MSC_INFO.NAMES.Values.ToArray());
                else
                    p = new CodeEditor(e.Node as ScriptNode);

            }
            else if (e.Node is ParamListNode)
                p = new ParamEditor(e.Node as ParamListNode);

            if (p != null)
            {
                p.Name = e.Node.Text + e.Node.Index;
                p.Text = e.Node.Text;
                if (tabControl1.TabPages.ContainsKey(p.Name))
                {
                    tabControl1.SelectTab(p.Name);
                    return;
                }
                else
                {
                    tabControl1.TabPages.Insert(0, p);
                    tabControl1.SelectTab(0);
                }
            }
        }
        private void FileTree_NodeMouseClick(object sender, TreeNodeMouseClickEventArgs e)
        {
            FileTree.SelectedNode = FileTree.GetNodeAt(e.Location);
        }

        private void tabControl1_MouseClick(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < tabControl1.TabCount; i++)
            {
                Rectangle r = tabControl1.GetTabRect(i);
                Rectangle closeButton = new Rectangle(r.Right - 18, r.Top + 3, 13, Font.Height);
                if (closeButton.Contains(e.Location))
                {
                    TabPage p = null;
                    if (tabControl1.TabPages[i] is CodeEditor)
                    {
                        p = tabControl1.TabPages[i] as CodeEditor;
                        TabControl tmp = (TabControl)p.Controls[0];
                        for (int x = 0; x < tmp.TabCount; x++)
                        {
                            ITS_EDITOR box = (ITS_EDITOR)tmp.TabPages[x].Controls[0];
                            box.ApplyChanges();
                        }
                    }
                    else if (tabControl1.TabPages[i] is ParamEditor)
                    {
                        var tmp = tabControl1.TabPages[i] as ParamEditor;
                        p = tmp;
                        var node = tmp.Node;
                        var file = Manager.Projects.Values[0].Fighter_Param_vl;
                        ((ParamGroup)file.Groups[node.Group]).Chunks[node.Entry] = node.Parameters.ToArray();
                    }
                    if (p != null)
                        tabControl1.TabPages.Remove(p);
                    return;
                }
            }
        }
        private void parseAnimationsToolStripMenuItem_Click(object sender, EventArgs e)
        {
            if (fsDlg.ShowDialog() == DialogResult.OK)
            {
                FileTree.BeginUpdate();

                var dict = Runtime.ParseAnimations(fsDlg.SelectedPath);
                TreeNode n;
                if ((n = FileTree.Nodes.Find("nACMD", false).FirstOrDefault()) != null)
                {
                    foreach (ScriptNode node in n.Nodes)
                    {
                        if (dict.ContainsKey(node.Identifier))
                            node.Text = dict[node.Identifier];
                    }
                }

                FileTree.EndUpdate();
            }
        }

        public enum IDE_MODE
        {
            File,
            Fighter,
            Project
        }
    }
}