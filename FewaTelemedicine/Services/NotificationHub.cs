﻿using FewaTelemedicine.Common;
using FewaTelemedicine.Domain;
using FewaTelemedicine.Domain.Models;
using FewaTelemedicine.Domain.Services;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;

namespace FewaTelemedicine.Services
{

    public class NotificationHub : Hub<INotificationHub>
    {
        WaitingRoom waitingroom = null;
        List<Provider> providers = null;
        List<ProviderCabin> providerCabins = null;
        FewaDbContext fewaDbContext = null;
	    [Obsolete]
        private IHostingEnvironment _hostingEnvironment;
	    [Obsolete]
        public NotificationHub(WaitingRoom _waitingroom,
                                List<Provider> _providers,
                                List<ProviderCabin> _providerCabins,
                                FewaDbContext _fewaDbContext,
				IHostingEnvironment hostingEnvironment)
        {
            fewaDbContext = _fewaDbContext;
            waitingroom = _waitingroom;
            providers = _providers;
            providerCabins = _providerCabins;
            _hostingEnvironment = hostingEnvironment;
        }

        // This attaches user with Signalr connection id
        private void AttachUser(string userName, string connectionId)
        {
            var claims = Context.User.Claims;
            // Boolean isDoctor = true;

            if (IsProvider())
            {
                foreach (var item in providers)
                {
                    if (item.userName == userName)
                    {
                        item.signalRConnectionId = connectionId;
                        return;
                    }
                }
            }
            else
            {
                foreach (var item in waitingroom.patients)
                {
                    if (item.name == userName)
                    {
                        item.signalRConnectionId = connectionId;
                        return;
                    }
                }
            }
            // This is where doctor is associated with Signalr ID


        }
        private void RemoveProvider(string userName)
        {

            foreach (var item in providers)
            {
                if (item.userName == userName)
                {
                    item.signalRConnectionId = null;
                    return;
                }
            }

        }
        // This removes the user when the disconnect event happens
        private void RemoveUser(string userName)
        {
            // You can not remove from live from collection
            // While browsing so you need temp.
            Patient temp = null;

            foreach (var item in waitingroom.patients)
            {
                if (item.name == userName)
                {
                    item.signalRConnectionId = null;
                    temp = item;
                }
            }
            if (temp != null)
            {
                waitingroom.patients.Remove(temp);
            }
            else
            {
                RemoveProvider(userName);
            }
        }
        private Patient GetPatientbyName(string PatName)
        {
            foreach (var t in waitingroom.patients)
            {
                if (PatName == t.name)
                {
                    return t;
                }
            }
            return null;
        }
        private Provider GetProviderByName(string providerName)
        {
            foreach (var t in providers)
            {
                if (providerName == t.userName)
                {
                    return t;
                }
            }
            return null;
        }

        private Boolean IsProvider()
        {
            var claims = Context.User.Claims;
            string usertype = claims.FirstOrDefault(c => c.Type == "UserType")?.Value;
            if (usertype == "provider") { return true; }
            return false;
        }

        private void SendUpdatedPatients()
        {
            var json = JsonConvert.SerializeObject(waitingroom.patients);

            foreach (var item in providers)
            {
                if (!(item.signalRConnectionId is null))
                {

                    this.Clients.Clients(GetProviderByName(item.userName)
                         .signalRConnectionId)
                             .GetAllPatients(json);

                }
            }
        }
        private void SendUpdatedProviders()
        {
            var activeProviders = providers.Where(a => a.signalRConnectionId != null).ToList();
            var jsonStr = JsonConvert.SerializeObject(activeProviders);

            foreach (var pat in waitingroom.patients)
            {
                this.Clients.Clients(pat.signalRConnectionId).GetAllProviders(jsonStr);
            }
        }
        private ProviderCabin GetCurrentProviderCabin()
        {
            foreach (var item in providerCabins)
            {
                if (item.provider.userName== Context.User.Identity.Name)
                {
                    return item;
                }

            }
            return null;
        }


        public override Task OnConnectedAsync()
        {
            AttachUser(Context.User.Identity.Name,
                Context.ConnectionId);
	        
            // over here send message to all doctor that pateint has logged

            SendUpdatedPatients();
            if (IsProvider())
            {
                SendUpdatedProviders();
            }
            return base.OnConnectedAsync();
        }
        public override Task OnDisconnectedAsync(Exception exception)
        {
            RemoveUser(Context.User.Identity.Name);
            string folderName = "Upload";
            string webRootPath = _hostingEnvironment.WebRootPath;
            string newPath = Path.Combine(webRootPath, folderName);
            if (Directory.Exists(newPath))
            {
                DirectoryInfo di = new DirectoryInfo(newPath);
                foreach (FileInfo dir in di.GetFiles())
                {
                    dir.Delete();
                }
                foreach (DirectoryInfo dir in di.GetDirectories())
                {
                    dir.Delete(true);
                }
            }
            SendUpdatedPatients();
            if (IsProvider())
            {
                SendUpdatedProviders();
            }
            return base.OnDisconnectedAsync(exception);
        }

        public async Task GetPatientAll()
        {
            // Only doctors can see patients and not patients
            if (IsProvider())
            {
                var json = JsonConvert.SerializeObject(waitingroom.patients);
                await this.Clients.Clients(GetProviderByName(Context.User.Identity.Name)
                        .signalRConnectionId)
                            .GetAllPatients(json);
            }

        }

        public async Task GetActiveProviders()
        {
            var activeDr = providers.Where(a => a.signalRConnectionId != null).ToList();
            var jsonStr = JsonConvert.SerializeObject(activeDr);
            await this.Clients.Client(GetPatientbyName(Context.User.Identity.Name).signalRConnectionId).GetAllProviders(jsonStr);
        }

        public async Task PatientCall(Patient obj)
        {
            Patient p = GetPatientbyName(obj.name);
            var param = fewaDbContext.practices.Select(a=>a.callingPlatform);

            if (p is null)
            {
                return;
            }
            else
            {
               
                p.status = (int)TeleConstants.PatientCalled;
                p.providerNameAttending = GetProviderByName(Context.User.Identity.Name).userName;

                p.appointmentDate = DateTime.Now;
                p.lastUpdated = DateTime.Now;
                p.startTime = DateTime.Now;
               
          
                
                GetCurrentProviderCabin().patient = p;
                var patient = JsonConvert.SerializeObject(p);

                SendUpdatedPatients();
                await this.Clients.Clients(GetPatientbyName(obj.name).signalRConnectionId)
                    .CallPatient(patient);
                await this.Clients.Clients(GetProviderByName(Context.User.Identity.Name).signalRConnectionId)
                     .CallPatient(patient);

            }
        }

        public async Task PatientAttended(Patient obj)
        {
            try
            {
                Patient p = GetPatientbyName(obj.name);
                if (p is null)
                {
                    return;
                }
                else
                {
                    GetCurrentProviderCabin().patient = new Patient();
                    p.status = (int)TeleConstants.PatientCompleted;
                    p.labOrdersSent = obj.labOrdersSent;
                    p.newPrescriptionsSentToYourPharmacy = obj.newPrescriptionsSentToYourPharmacy;
                    p.newPrescriptionsMailedToYou = obj.newPrescriptionsMailedToYou;
                    p.endTime = DateTime.Now;
                    p.medication = obj.medication;
                    p.followUpNumber = obj.followUpNumber;
                    p.followUpMeasure = obj.followUpMeasure;
                    p.url = obj.url;
                   
                   


                    var patient = JsonConvert.SerializeObject(p);
                    await this.Clients.Clients(GetPatientbyName(obj.name).signalRConnectionId)
                      .CompletePatient(patient);

                    waitingroom.patients.Remove(p);
                    SendUpdatedPatients();

                    fewaDbContext.patients.Add(p);
                    fewaDbContext.SaveChanges();

                }
            }
            catch(Exception ex)
            {
                throw ex;
            }
        }

        public async Task SendChatMessage(ChatMessage chatMessage)
        {
            string connId = "";
            ChatMessage chatMsg = new ChatMessage();
            if (chatMessage.isProvider)
            {
                connId = GetProviderByName(chatMessage.name).signalRConnectionId;

                chatMsg.isProvider = false;
                chatMsg.name = GetPatientbyName(Context.User.Identity.Name).name;
            }
            else
            {
                connId = GetPatientbyName(chatMessage.name).signalRConnectionId;

                chatMsg.isProvider = true;
                chatMsg.name = GetProviderByName(Context.User.Identity.Name).userName;
            }
            chatMsg.message = chatMessage.message;

            var chatMsgJsonStr = JsonConvert.SerializeObject(chatMsg);
            await this.Clients.Client(connId).ChatMessage(chatMsgJsonStr);
        }

    }


    public class Message
    {
        public string Type { get; set; }
        public string Payload { get; set; }
    }
}

