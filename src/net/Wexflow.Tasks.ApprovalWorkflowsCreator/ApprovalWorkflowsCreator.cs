﻿using System;
using System.Linq;
using System.Security;
using System.Threading;
using System.Xml.Linq;
using Wexflow.Core;

namespace Wexflow.Tasks.ApprovalWorkflowsCreator
{
    public class ApprovalWorkflowsCreator : Task
    {
        private static string smKey = "ApprovalRecordsCreator.RecordIds";

        public string AssignedTo { get; private set; }
        public string Approver { get; private set; }

        public ApprovalWorkflowsCreator(XElement xe, Workflow wf) : base(xe, wf)
        {
            AssignedTo = GetSetting("assignedTo");
            Approver = GetSetting("approver");
        }

        public override TaskStatus Run()
        {
            Info("Importing records...");

            var success = true;
            var atLeastOneSuccess = false;

            try
            {

                if (!SharedMemory.ContainsKey(smKey))
                {
                    Error($"Shared memory key {smKey} not found.");
                    success = false;
                }

                var recordIds = (string[])SharedMemory[smKey];

                foreach (var recordId in recordIds)
                {
                    try
                    {
                        var record = Workflow.Database.GetRecord(recordId);
                        var workflowId = Workflow.WexflowEngine.Workflows.Select(w => w.Id).Max() + 1;
                        var workflowName = $"Workflow_ApproveRecord_{SecurityElement.Escape(Approver)}_{SecurityElement.Escape(record.Name)}";

                        var xml = $"<Workflow xmlns='urn:wexflow-schema' id='{workflowId}' name='{workflowName}' description='{workflowName}'>\r\n"
                                + "	 <Settings>\r\n"
                                + "    <Setting name='launchType' value='trigger' />\r\n"
                                + "    <Setting name='enabled' value='true' />\r\n"
                                + "    <Setting name='approval' value='true' />\r\n"
                                + "    <Setting name='enableParallelJobs' value='true' />\r\n"
                                + "	  </Settings>\r\n"
                                + "	 <LocalVariables />\r\n"
                                + "	 <Tasks>\r\n"
                               + $"    <Task id='1' name='ApproveRecord' description='Approving record {SecurityElement.Escape(record.Name)}' enabled='true'>\r\n"
                               + $"      <Setting name='record' value='{SecurityElement.Escape(recordId)}' />\r\n"
                               + $"      <Setting name='assignedTo' value='{SecurityElement.Escape(AssignedTo)}' />\r\n"
                                + "    </Task>\r\n"
                                + "  </Tasks>\r\n"
                                + "</Workflow>\r\n";

                        var approver = Workflow.WexflowEngine.GetUser(Approver);
                        var worflowId = Workflow.WexflowEngine.SaveWorkflow(approver.GetDbId(), approver.UserProfile, xml, false);

                        if (worflowId != "-1")
                        {
                            var workflow = Workflow.WexflowEngine.GetWorkflow(workflowId);
                            workflow.StartAsync(Approver);
                            Info($"Approval Workflow of the record {recordId} - {record.Name} created and started successfully.");
                        }
                        else
                        {
                            Error($"An error occured while creating the approval workflow of the record {recordId} - {record.Name}.");
                            success = false;
                        }

                        if (success && !atLeastOneSuccess) atLeastOneSuccess = true;
                    }
                    catch (ThreadAbortException)
                    {
                        throw;
                    }
                    catch (Exception e)
                    {
                        ErrorFormat("An error occured while importing the record {0}.", e, recordId);
                        success = false;
                    }
                }
            }
            catch (ThreadAbortException)
            {
                throw;
            }
            catch (Exception e)
            {
                ErrorFormat("An error occured while importing records.", e);
                success = false;
            }

            var status = Status.Success;

            if (!success && atLeastOneSuccess)
            {
                status = Status.Warning;
            }
            else if (!success)
            {
                status = Status.Error;
            }

            Info("Task finished.");
            return new TaskStatus(status);
        }
    }
}