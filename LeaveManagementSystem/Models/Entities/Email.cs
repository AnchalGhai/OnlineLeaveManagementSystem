// Models/EmailHelper.cs
using System.Net;
using System.Net.Mail;
using LeaveManagementSystem.Models.Entities;

namespace LeaveManagementSystem.Models
{
    public static class Email
    {
        private static string _fromEmail = "projectleavemanagement12@gmail.com";
        private static string _appPassword = "ibuuadahpcfwqdoe";
        private static string _fromName = "Leave Pro";

        // ✅ 1. Employee applied for leave → Confirmation to Employee
        public static void SendEmployeeApplied(string employeeEmail, string employeeName, LeaveApplication leave)
        {
            var subject = $"Leave Application Submitted - #{leave.LeaveId}";
            var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px;'>
                <h3 style='color: #2c3e50;'>Leave Application Submitted</h3>
                <p>Dear <strong>{employeeName}</strong>,</p>   
                <p>Your leave application has been submitted successfully and is pending approval from your manager.</p>
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0;'>
                    <h4 style='color: #3498db;'>Application Details:</h4>
                    <ul style='list-style: none; padding-left: 0;'>
                        <li><strong>Application ID:</strong> #{leave.LeaveId}</li>
                        <li><strong>Leave Type:</strong> {leave.LeaveType?.Name ?? "N/A"}</li>
                        <li><strong>From:</strong> {leave.StartDate:dd-MMM-yyyy}</li>
                        <li><strong>To:</strong> {leave.EndDate:dd-MMM-yyyy}</li>
                        <li><strong>Total Days:</strong> {leave.TotalDays}</li>
                        <li><strong>Status:</strong> <span style='color: #e67e22; font-weight: bold;'>Pending Approval</span></li>
                    </ul>
                </div>
                
                
                <p style='color: #7f8c8d; font-size: 12px;'>
                    Regards,<br/>
                    <strong>Leave Pro</strong>
                </p>
            </div>";

            SendEmail(employeeEmail, subject, body);
        }

        // ✅ 2. Manager action → Notification to Employee
        public static void SendEmployeeStatus(string employeeEmail, string employeeName, LeaveApplication leave, string managerName)
        {
            var statusColor = leave.Status == "Approved" ? "#27ae60" : "#e74c3c";
            var action = leave.Status == "Approved" ? "approved" : "rejected";
            var subject = $"Leave Application {leave.Status} - #{leave.LeaveId}";

            var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px;'>
                <h3 style='color: #2c3e50;'>Leave Application {leave.Status}</h3>
                <p>Dear <strong>{employeeName}</strong>,</p>
                
                <p>Your leave application has been <strong style='color:{statusColor}'>{action}</strong> by your manager.</p>
                
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0;'>
                    <h4 style='color: {statusColor};'>Application Details:</h4>
                    <ul style='list-style: none; padding-left: 0;'>
                        <li><strong>Application ID:</strong> #{leave.LeaveId}</li>
                        <li><strong>Leave Type:</strong> {leave.LeaveType?.Name ?? "N/A"}</li>
                        <li><strong>Period:</strong> {leave.StartDate:dd-MMM-yyyy} to {leave.EndDate:dd-MMM-yyyy}</li>
                        <li><strong>Manager Comments:</strong> {leave.ManagerComments ?? "No comments provided"}</li>
                        <li><strong>Action Date:</strong> {DateTime.Now:dd-MMM-yyyy HH:mm}</li>
                    </ul>
                </div>
                <p style='color: #7f8c8d; font-size: 12px;'>
                    Regards,<br/>
                    <strong>Leave Pro</strong>
                </p>
            </div>";

            SendEmail(employeeEmail, subject, body);
        }

        // ✅ 3. Manager applied for leave → Confirmation to Manager
        public static void SendManagerApplied(string managerEmail, string managerName, LeaveApplication leave)
        {
            var subject = $"Your Leave Application Submitted - #{leave.LeaveId}";
            var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px;'>
                <h3 style='color: #2c3e50;'>Manager Leave Application Submitted</h3>
                <p>Dear <strong>{managerName}</strong>,</p>
                
                <p>Your leave application has been submitted successfully and is pending approval from Admin.</p>
                
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0;'>
                    <h4 style='color: #3498db;'>Your Application Details:</h4>
                    <ul style='list-style: none; padding-left: 0;'>
                        <li><strong>Application ID:</strong> #{leave.LeaveId}</li>
                        <li><strong>Leave Type:</strong> {leave.LeaveType?.Name ?? "N/A"}</li>
                        <li><strong>From:</strong> {leave.StartDate:dd-MMM-yyyy}</li>
                        <li><strong>To:</strong> {leave.EndDate:dd-MMM-yyyy}</li>
                        <li><strong>Total Days:</strong> {leave.TotalDays}</li>
                        <li><strong>Reason:</strong> {leave.Reason}</li>
                        <li><strong>Status:</strong> <span style='color: #e67e22; font-weight: bold;'>Pending Admin Approval</span></li>
                    </ul>
                </div>
                
               
                <p style='color: #7f8c8d; font-size: 12px;'>
                    Regards,<br/>
                    <strong>Leave Pro</strong>
                </p>
            </div>";

            SendEmail(managerEmail, subject, body);
        }

        // ✅ 4. Admin action → Notification to Manager
        public static void SendManagerStatus(string managerEmail, string managerName, LeaveApplication leave, string adminName)
        {
            var statusColor = leave.Status == "Approved" ? "#27ae60" : "#e74c3c";
            var action = leave.Status == "Approved" ? "approved" : "rejected";
            var subject = $"Your Leave Application {leave.Status} - #{leave.LeaveId}";

            var body = $@"
            <div style='font-family: Arial, sans-serif; padding: 20px;'>
                <h3 style='color: #2c3e50;'>Your Leave Application {leave.Status}</h3>
                <p>Dear <strong>{managerName}</strong>,</p>
                
                <p>Your leave application has been <strong style='color:{statusColor}'>{action}</strong> by Admin.</p>
                
                <div style='background-color: #f8f9fa; padding: 15px; border-radius: 5px; margin: 15px 0;'>
                    <h4 style='color: {statusColor};'>Application Details:</h4>
                    <ul style='list-style: none; padding-left: 0;'>
                        <li><strong>Application ID:</strong> #{leave.LeaveId}</li>
                        <li><strong>Leave Type:</strong> {leave.LeaveType?.Name ?? "N/A"}</li>
                        <li><strong>Period:</strong> {leave.StartDate:dd-MMM-yyyy} to {leave.EndDate:dd-MMM-yyyy}</li>
                        <li><strong>Admin Comments:</strong> {leave.ManagerComments ?? "No comments provided"}</li>
                        <li><strong>Action Date:</strong> {DateTime.Now:dd-MMM-yyyy HH:mm}</li>
                    </ul>
                </div>
                <p style='color: #7f8c8d; font-size: 12px;'>
                    Regards,<br/>
                    <strong>Leave Pro</strong>
                </p>
            </div>";

            SendEmail(managerEmail, subject, body);
        }

        private static void SendEmail(string toEmail, string subject, string body)
        {
            try
            {
                Console.WriteLine($"Attempting to send email to: {toEmail}");

                var smtpClient = new SmtpClient("smtp.gmail.com", 587)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(_fromEmail, _appPassword)
                };

                var mailMessage = new MailMessage
                {
                    From = new MailAddress(_fromEmail, _fromName),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

                mailMessage.To.Add(toEmail);

                // Fire and forget
                Task.Run(() => smtpClient.SendMailAsync(mailMessage));

                Console.WriteLine($" Email queued for: {toEmail}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($" Email failed to {toEmail}: {ex.Message}");
            }
        }
    }
}