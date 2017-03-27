using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using System.Threading;
using FS.SpareStores.DAL.Model;
using FS.SpareStores.DAL.Helpers;
using Microsoft.WindowsAzure.Storage;
using System.Configuration;
using Microsoft.WindowsAzure.Storage.Blob;
using System.IO;
using System.Diagnostics;
using System.Net;

namespace DeldoSynchWebJob
{
    public class TempRigaCSV
    {
        public Double costo { get; set; }
        public Double costoIVA { get; set; }
        public double corriere { get; set; }
        public double ricarico { get; set; }
        public double prezzoVendita { get; set; }
        public string Modello { get; set; }
        public int BattistradaResiduo { get; set; }
        public string DOT { get; set; }
    }

    // To learn more about Microsoft Azure WebJobs SDK, please see http://go.microsoft.com/fwlink/?LinkID=320976
    class Program
    {
        // Please set the following connection strings in app.config for this WebJob to run:
        // AzureWebJobsDashboard and AzureWebJobsStorage
        static void Main()
        {
            while (true)
            {
                try
                {
                    ImportDeldoFiles();
                    ImportAstigianaFiles();
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.ToString());
                    LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Errore importazione prodotti DELDO: {0}", ex.ToString()));
                }

                Thread.Sleep(60000);
            }
        }

        private static void ImportAstigianaFiles()
        {
            int numRiga = 0;
            try
            {
                DBModel db = new DBModel();
                ImportAstigiana importSettings = db.ImportAstigiana.FirstOrDefault();

                if (importSettings == null)
                {
                    LogHelper.LogData(LogEntry.AstigianaWebJob, string.Format("Errore importazione prodotti ASTIGIANA GOMME: impostazioni non presenti"));
                    return;
                }

                // Carico tutti gli ID dei prodotti sincronizzati, per verificare che siano ancora presenti
                Dictionary<int, bool> dictProdottiAstigiana = new Dictionary<int, bool>();

                foreach (var idProd in db.ProdottiPneumatici.Where(x => x.IDAstigianaGomme.HasValue).Select(x => x.IDAstigianaGomme))
                {
                    dictProdottiAstigiana.Add(idProd.Value, false); // Imposto a false e man mano che li trovo nel CSV li segno come presenti
                }


                // Verifico se il file FTP è stato aggiornato rispetto all'ultima verifica
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(string.Format("ftp://{0}/{1}", importSettings.FTPServerURL, importSettings.Filename));
                request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
                request.Credentials = new NetworkCredential(importSettings.FTPUser, importSettings.FTPPass);
                FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                DateTime dataAggiornamentoFile;
                int prodottiCreati = 0, caratteristicheProdottoCreate = 0, marchiCreati = 0, disponibilitaCreate = 0, disponibilitaAggiornate = 0;

                if (response.LastModified > importSettings.FileLastUpdate)
                {
                    dataAggiornamentoFile = response.LastModified;
                    response.Close();
                    request = (FtpWebRequest)WebRequest.Create(string.Format("ftp://{0}/{1}", importSettings.FTPServerURL, importSettings.Filename));
                    request.Credentials = new NetworkCredential(importSettings.FTPUser, importSettings.FTPPass);
                    request.Method = WebRequestMethods.Ftp.DownloadFile;
                    response = (FtpWebResponse)request.GetResponse();

                    LogHelper.LogData(LogEntry.AstigianaWebJob, string.Format("File CSV modificato in data {0: dd/MM/yyyy hh:mm}, eseguo aggiornamento", dataAggiornamentoFile));
                    Stream responseStream = response.GetResponseStream();

                    // Salvo il file su blob Azure
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
                    // Create the blob client.
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                    // Retrieve reference to a previously created container.
                    CloudBlobContainer container = blobClient.GetContainerReference("upload-astigiana");
                    // Retrieve reference to a blob named "photo1.jpg".
                    CloudBlockBlob blockBlob = container.GetBlockBlobReference(
                        string.Format("ImportAstigiana_{0}{1}{2}{3}{4}",
                        dataAggiornamentoFile.Year, dataAggiornamentoFile.Month, dataAggiornamentoFile.Day, dataAggiornamentoFile.Hour, dataAggiornamentoFile.Minute));

                    blockBlob.UploadFromStream(responseStream);
                    responseStream.Close();


                    Brand marchio;
                    int idAstigiana;
                    CaratteristicaProdotto larghezza, altezza, diametro, indiceCarico, indiceVelocita, stagione;
                    CategoriaProdotto categoria;
                    string modello, EAN, valStagione, valVeicolo;
                    bool runflat;
                    bool XL;
                    double prezzo;

                    var ms = new MemoryStream();
                    blockBlob.DownloadToStream(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    StreamReader reader = new StreamReader(ms);


                    while (!reader.EndOfStream)
                    {
                        var riga = reader.ReadLine();

                        if (numRiga > 0 && !string.IsNullOrWhiteSpace(riga))
                        {
                            var tokens = riga.Split(';');

                            idAstigiana = Convert.ToInt32(tokens[0]);

                            if (dictProdottiAstigiana.ContainsKey(idAstigiana))
                                dictProdottiAstigiana[idAstigiana] = true;
                            //Console.WriteLine(string.Format("Id Astigiana: {0}", idAstigiana));

                            var prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.IDAstigianaGomme == idAstigiana);

                            if (prodotto == null)
                            {
                                // Non esistente o non associato, provo a cercarlo per EAN altrimenti lo creo
                                EAN = tokens[23];

                                if (EAN.Length == 13)
                                {
                                    prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.EAN13 == EAN);
                                }
                                else
                                {
                                    Console.WriteLine(string.Format("EAN non valido: {0}", EAN));
                                }

                                if (prodotto == null)
                                {
                                    prodottiCreati++;

                                    // Non esiste su gestionale, prendo tutti i parametri e lo creo
                                    string valMarchio = tokens[1];
                                    marchio = db.Brand.FirstOrDefault(x => x.Nome == valMarchio);
                                    if (marchio == null)
                                    {
                                        marchio = new Brand() { Nome = valMarchio, DataUltimoAggiornamento = DateTime.Now };
                                        db.Brand.Add(marchio);
                                        marchiCreati++;
                                    }

                                    string valLarghezza = tokens[2];
                                    larghezza = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Larghezza && x.Valore == valLarghezza);
                                    if (larghezza == null)
                                    {
                                        larghezza = new CaratteristicaProdotto()
                                        {
                                            TipoCaratteristica = CaratteristicaProdotto.Larghezza,
                                            DataUltimoAggiornamento = DateTime.Now,
                                            Valore = valLarghezza
                                        };
                                        db.CaratteristicheProdotti.Add(larghezza);
                                        caratteristicheProdottoCreate++;
                                    }

                                    string valAltezza = tokens[3];
                                    altezza = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Altezza && x.Valore == valAltezza);
                                    if (altezza == null)
                                    {
                                        altezza = new CaratteristicaProdotto()
                                        {
                                            TipoCaratteristica = CaratteristicaProdotto.Altezza,
                                            DataUltimoAggiornamento = DateTime.Now,
                                            Valore = valAltezza
                                        };
                                        db.CaratteristicheProdotti.Add(altezza);
                                        caratteristicheProdottoCreate++;
                                    }

                                    string valDiametro = tokens[5];
                                    diametro = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Diametro && x.Valore == valDiametro);
                                    if (diametro == null)
                                    {
                                        diametro = new CaratteristicaProdotto()
                                        {
                                            TipoCaratteristica = CaratteristicaProdotto.Diametro,
                                            DataUltimoAggiornamento = DateTime.Now,
                                            Valore = valDiametro
                                        };
                                        db.CaratteristicheProdotti.Add(diametro);
                                        caratteristicheProdottoCreate++;
                                    }

                                    string valIndiceCarico = tokens[6];
                                    indiceCarico = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceCarico && x.Valore == valIndiceCarico);
                                    if (indiceCarico == null)
                                    {
                                        indiceCarico = new CaratteristicaProdotto()
                                        {
                                            TipoCaratteristica = CaratteristicaProdotto.IndiceCarico,
                                            DataUltimoAggiornamento = DateTime.Now,
                                            Valore = valIndiceCarico
                                        };
                                        db.CaratteristicheProdotti.Add(indiceCarico);
                                        caratteristicheProdottoCreate++;
                                    }

                                    string valIndiceVelocita = tokens[7];
                                    indiceVelocita = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceVelocita && x.Valore == valIndiceVelocita);
                                    if (indiceVelocita == null)
                                    {
                                        indiceVelocita = new CaratteristicaProdotto()
                                        {
                                            TipoCaratteristica = CaratteristicaProdotto.IndiceVelocita,
                                            DataUltimoAggiornamento = DateTime.Now,
                                            Valore = valIndiceVelocita
                                        };
                                        db.CaratteristicheProdotti.Add(indiceVelocita);
                                        caratteristicheProdottoCreate++;
                                    }

                                    modello = tokens[8];
                                    runflat = tokens[9] == "Y";
                                    XL = tokens[16] == "XL";
                                    switch (tokens[20])
                                    {
                                        case "Summer":
                                            valStagione = "ESTIVO";
                                            break;
                                        case "Winter":
                                            valStagione = "INVERNALE";
                                            break;
                                        case "Four Season":
                                        default:
                                            valStagione = "ALL SEASON";
                                            break;
                                    }
                                    stagione = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Stagione && x.Valore == valStagione);

                                    switch (tokens[21])
                                    {
                                        case "AUTOCARRO":
                                            valVeicolo = "AUTOCARRO";
                                            break;
                                        case "TRASPORTO LEGGERO":
                                            valVeicolo = "FURGONE";
                                            break;
                                        case "Fuoristrada, Suv e 4x4":
                                            valVeicolo = "SUV 4X4";
                                            break;
                                        case "MOTO":
                                        case "SCOOTER":
                                            valVeicolo = "MOTO E SCOOTER";
                                            break;
                                        case "VETTURA":
                                        default:
                                            valVeicolo = "AUTO";
                                            break;
                                    }

                                    categoria = db.CategorieProdotti.FirstOrDefault(x => x.Nome == valVeicolo);

                                    prodotto = new Prodotto_Pneumatici();
                                    prodotto.DataCaricamento = DateTime.Now;
                                    prodotto.DataUltimaModifica = DateTime.Now;
                                    prodotto.EAN13 = EAN;
                                    prodotto.IDAstigianaGomme = idAstigiana;
                                    prodotto.Modello = modello;
                                    prodotto.refAltezza = altezza;
                                    prodotto.refBrand = marchio;
                                    prodotto.refCategoria = categoria;
                                    prodotto.refDiametro = diametro;
                                    prodotto.refIndiceCarico = indiceCarico;
                                    prodotto.refIndiceVelocita = indiceVelocita;
                                    prodotto.refLarghezza = larghezza;
                                    prodotto.refStagione = stagione;
                                    prodotto.Descrizione = string.Format("{0} {1}/{2}R{3} {4}{5} {6} {7}",
                                        marchio.Nome, larghezza.Valore, altezza.Valore, diametro.Valore,
                                        indiceCarico.Valore, indiceVelocita.Valore, modello, stagione.Valore);

                                    db.ProdottiPneumatici.Add(prodotto);
                                }
                            }

                            var disponibilita = prodotto.LstDisponibilitaProdotto.FirstOrDefault(x => x.FK_Fornitore == importSettings.FK_Fornitore && x.FK_Magazzino == importSettings.FK_Magazzino);

                            if (disponibilita == null)
                            {
                                disponibilita = new Disponibilita();
                                disponibilita.AliquotaIVA = 22;
                                disponibilita.CostoCorriere = 6;
                                disponibilita.FK_Fornitore = importSettings.FK_Fornitore;
                                disponibilita.FK_Magazzino = importSettings.FK_Magazzino;
                                disponibilita.refPneumatico = prodotto;
                                disponibilitaCreate++;
                            }
                            else
                            {
                                disponibilitaAggiornate++;
                            }

                            if (Debugger.IsAttached)
                                prezzo = Convert.ToDouble(tokens[29].Replace(".", ","));
                            else
                                prezzo = Convert.ToDouble(tokens[29].Replace(",", "."));
                            disponibilita.Costo = prezzo;
                            disponibilita.AliquotaIVA = 22;
                            disponibilita.CostoIVA = disponibilita.Costo * (1 + disponibilita.AliquotaIVA / 100);
                            disponibilita.UltimoAggiornamento = DateTime.Now;
                            disponibilita.Quantita = Convert.ToInt32(tokens[22]);
                            disponibilita.CostoCorriereIVA = importSettings.CostoCorriereIVA;
                            disponibilita.CostoCorriere = disponibilita.CostoCorriereIVA * 100 / (100 + disponibilita.AliquotaIVA);
                            disponibilita.MUValoreIVA = importSettings.MarkupEuro;
                            disponibilita.MUValore = importSettings.MarkupEuro * 100 / (100 + disponibilita.AliquotaIVA);
                            disponibilita.MUPerc = importSettings.MarkupPercentuale;
                            disponibilita.CostoIntermediario = importSettings.PercIntermediario;

                            double prezzoTemp = (disponibilita.Costo + disponibilita.CostoCorriere) * (1 + disponibilita.MUPerc / 100) + disponibilita.MUValore;
                            disponibilita.Prezzo = prezzoTemp * 100 / (100 - disponibilita.CostoIntermediario);
                            disponibilita.PrezzoIVA = disponibilita.Prezzo * (1 + disponibilita.AliquotaIVA / 100);

                            if (disponibilita.Id == 0)
                            {
                                db.DisponibilitaProdotti.Add(disponibilita);
                            }
                        }
                        db.SaveChanges();

                        numRiga++;
                        Console.WriteLine(numRiga);
                    }

                    importSettings.FileLastUpdate = dataAggiornamentoFile;
                    db.SaveChanges();
                    reader.Close();

                    int dispAzzerate = 0;
                    foreach (var id in dictProdottiAstigiana.Where(x => x.Value == false))
                    {
                        var prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.IDAstigianaGomme == id.Key);

                        if (prodotto != null)
                        {
                            var disp = prodotto.LstDisponibilitaProdotto.FirstOrDefault(x => x.FK_Fornitore == importSettings.FK_Fornitore && x.FK_Magazzino == importSettings.FK_Magazzino);

                            if (disp != null)
                            {
                                disp.Quantita = 0;
                                dispAzzerate++;
                            }
                        }
                    }

                    LogHelper.LogData(LogEntry.AstigianaWebJob, string.Format(
                        "Sincronizzazione ASTIGIANA GOMME ESEGUITA. Prodotti creati: {0} - Marchi creati: {1} - Caratteristiche create: {2}, Disp. create: {3}, Disp. aggiornate: {4}, Disp. azzerate: {5}",
                        prodottiCreati, marchiCreati, caratteristicheProdottoCreate, disponibilitaCreate, disponibilitaAggiornate, dispAzzerate));

                    db.SaveChanges();
                }

            }
            catch (Exception ex)
            {
                LogHelper.LogData(LogEntry.AstigianaWebJob, string.Format("Errore importazione prodotti ASTIGIANA GOMME- riga: {1}: {0} ", ex.ToString(), numRiga));
                Console.WriteLine(ex.ToString());
            }
        }

        private static void ImportDeldoFiles()
        {
            int numRiga = 0;
            try
            {
                DBModel db = new DBModel();
                ImportDeldo importSettings = db.ImportDELDO.FirstOrDefault();

                LogHelper.ClearLog(LogEntry.DeldoWebJob);

                LogHelper.SetLogStatus(LogEntry.DeldoWebJob, false, true, "Sincronizzazione in corso");

                if (importSettings == null)
                {
                    LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Errore importazione prodotti DELDO: impostazioni non presenti"));
                    return;
                }

                Dictionary<string, bool> dictProdDeldo = new Dictionary<string, bool>();

                foreach (var idProd in db.ProdottiPneumatici.Where(x => x.IDDeldo != null).Select(x => x.IDDeldo))
                {
                    if (!dictProdDeldo.ContainsKey(idProd))
                        dictProdDeldo.Add(idProd, false);
                }

                // Verifico se il file FTP è stato aggiornato rispetto all'ultima verifica
                FtpWebRequest request = (FtpWebRequest)WebRequest.Create(string.Format("ftp://{0}/{1}", importSettings.FTPServerURL, importSettings.Filename));
                request.Method = WebRequestMethods.Ftp.GetDateTimestamp;
                request.Credentials = new NetworkCredential(importSettings.FTPUser, importSettings.FTPPass);
                FtpWebResponse response = (FtpWebResponse)request.GetResponse();
                DateTime dataAggiornamentoFile;
                int prodottiCreati = 0, caratteristicheProdottoCreate = 0, marchiCreati = 0, disponibilitaCreate = 0, disponibilitaAggiornate = 0;

                if (response.LastModified > importSettings.FileLastUpdate)
                {
                    dataAggiornamentoFile = response.LastModified;
                    response.Close();
                    request = (FtpWebRequest)WebRequest.Create(string.Format("ftp://{0}/{1}", importSettings.FTPServerURL, importSettings.Filename));
                    request.Credentials = new NetworkCredential(importSettings.FTPUser, importSettings.FTPPass);
                    request.Method = WebRequestMethods.Ftp.DownloadFile;
                    response = (FtpWebResponse)request.GetResponse();

                    LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("File CSV modificato in data {0: dd/MM/yyyy hh:mm}, eseguo aggiornamento", dataAggiornamentoFile));
                    Stream responseStream = response.GetResponseStream();

                    // Salvo il file su blob Azure
                    CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
                    // Create the blob client.
                    CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                    // Retrieve reference to a previously created container.
                    CloudBlobContainer container = blobClient.GetContainerReference("upload-deldo");
                    // Retrieve reference to a blob named "photo1.jpg".
                    CloudBlockBlob blockBlob = container.GetBlockBlobReference(
                        string.Format("ImportDeldo_{0}{1}{2}{3}{4}",
                        dataAggiornamentoFile.Year, dataAggiornamentoFile.Month, dataAggiornamentoFile.Day, dataAggiornamentoFile.Hour, dataAggiornamentoFile.Minute));

                    blockBlob.UploadFromStream(responseStream);
                    responseStream.Close();

                    var ms = new MemoryStream();
                    blockBlob.DownloadToStream(ms);
                    ms.Seek(0, SeekOrigin.Begin);
                    StreamReader reader = new StreamReader(ms);

                    while (!reader.EndOfStream)
                    {
                        string riga = reader.ReadLine();

                        if (numRiga != 0)
                        {
                            var tokens = riga.Split(';');

                            string idDeldo = tokens[0];

                            if (dictProdDeldo.ContainsKey(idDeldo))
                                dictProdDeldo[idDeldo] = true;

                            string EAN = tokens[18].Replace("\"", "");
                            string brand = tokens[1];
                            brand = brand.Replace(" ZO", "");
                            brand = brand.Replace(" WI", "");

                            string pattern = tokens[7];

                            string valStagione = string.Empty;
                            switch (tokens[13])
                            {
                                case "Summer":
                                    valStagione = "ESTIVO";
                                    break;
                                case "Winter":
                                    valStagione = "INVERNALE";
                                    break;
                                case "All Season":
                                    valStagione = "ALL SEASON";
                                    break;
                                default:
                                    //stagione non valida
                                    continue;
                            }

                            string valVeicolo = string.Empty;
                            switch (tokens[14])
                            {
                                case "Truck":
                                    valVeicolo = "AUTOCARRO";
                                    break;
                                case "Light Truck":
                                    valVeicolo = "FURGONE";
                                    break;
                                case "Jeep / 4x4":
                                    valVeicolo = "SUV 4X4";
                                    break;
                                case "Passenger Car":
                                default:
                                    valVeicolo = "AUTO";
                                    break;
                            }

                            var categoria = db.CategorieProdotti.FirstOrDefault(x => x.Nome == valVeicolo);
                            if (tokens[6].Length < 4)
                            {
                                LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Indice di velocità e carico non valido {0}, riga {1}. Riga ignorata", tokens[6], riga));
                                continue;

                            }
                            string valIndiceCarico = tokens[6].Substring(0, 3);
                            string valIndiceVelocita = tokens[6].Substring(3, 1);
                            valIndiceCarico.Replace("(", "");
                            var strAltezza = tokens[3];
                            var strLarghezza = tokens[2];
                            var strDiametro = tokens[5];

                            if (string.IsNullOrWhiteSpace(valIndiceCarico) ||
                                string.IsNullOrWhiteSpace(valIndiceVelocita) ||
                                string.IsNullOrWhiteSpace(strAltezza) ||
                                string.IsNullOrWhiteSpace(strLarghezza) ||
                                string.IsNullOrWhiteSpace(strDiametro))
                            {
                                LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Dati incompleti nella riga {0}. Riga ignorata", riga));
                                continue;
                            }

                            double costo = 0;
                            if (Debugger.IsAttached)
                                costo = Convert.ToDouble(tokens[16].Replace(".", ","));
                            else
                                costo = Convert.ToDouble(tokens[16].Replace(",", "."));
                            int quantita = Convert.ToInt32(tokens[15]);
                            Prodotto_Pneumatici prodotto;

                            //Cerco il prodotto su DB in base all'EAN
                            prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.EAN13 == EAN);

                            if (prodotto == null)
                            {
                                // Provo a caricarlo con l'ID di Deldo
                                prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.IDDeldo == idDeldo);
                            }

                            if (prodotto == null)
                            {
                                prodottiCreati++;

                                prodotto = new Prodotto_Pneumatici();
                                prodotto.DataCaricamento = DateTime.Now;
                                prodotto.DataUltimaModifica = DateTime.Now;
                                prodotto.EAN13 = EAN;
                                prodotto.Modello = pattern;

                                // Categoria (auto, moto, etc...)
                                prodotto.FK_Categoria = categoria.Id;

                                // Marca
                                var marca = db.Brand.FirstOrDefault(x => x.Nome == brand);
                                if (marca == null)
                                {
                                    marca = new Brand() { Nome = brand };
                                    db.Brand.Add(marca);
                                }
                                prodotto.refBrand = marca;

                                // Altezza
                                var altezza = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Altezza && x.Valore == strAltezza);
                                if (altezza == null)
                                {
                                    altezza = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Altezza, Valore = strAltezza };
                                    db.CaratteristicheProdotti.Add(altezza);
                                }
                                prodotto.refAltezza = altezza;

                                var larghezza = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Larghezza && x.Valore == strLarghezza);
                                if (larghezza == null)
                                {
                                    larghezza = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Larghezza, Valore = strLarghezza };
                                    db.CaratteristicheProdotti.Add(larghezza);
                                }
                                prodotto.refLarghezza = larghezza;

                                var diametro = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Diametro && x.Valore == strDiametro);
                                if (diametro == null)
                                {
                                    diametro = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Diametro, Valore = strDiametro };
                                    db.CaratteristicheProdotti.Add(diametro);
                                }
                                prodotto.refDiametro = diametro;

                                var stagione = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Stagione && x.Valore == valStagione);
                                if (stagione == null)
                                {
                                    stagione = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Stagione, Valore = valStagione };
                                    db.CaratteristicheProdotti.Add(stagione);
                                }
                                prodotto.refStagione = stagione;

                                var indiceCarico = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceCarico && x.Valore == valIndiceCarico);
                                if (indiceCarico == null)
                                {
                                    indiceCarico = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.IndiceCarico, Valore = valIndiceCarico };
                                    db.CaratteristicheProdotti.Add(indiceCarico);
                                }
                                prodotto.refIndiceCarico = indiceCarico;

                                var indiceVelocita = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceVelocita && x.Valore == valIndiceVelocita);
                                if (indiceVelocita == null)
                                {
                                    indiceVelocita = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.IndiceVelocita, Valore = valIndiceVelocita };
                                    db.CaratteristicheProdotti.Add(indiceVelocita);
                                }
                                prodotto.refIndiceVelocita = indiceVelocita;
                                prodotto.Descrizione = string.Format("{0} {1}/{2}R{3} {4}{5} {6} {7}",
                                        marca.Nome, larghezza.Valore, altezza.Valore, diametro.Valore,
                                        indiceCarico.Valore, indiceVelocita.Valore, pattern, stagione.Valore);

                                db.ProdottiPneumatici.Add(prodotto);
                            }

                            // Devo essere sicuro che ci sia l'ID di Deldo
                            prodotto.IDDeldo = idDeldo;

                            // Aggiunta della disponibilità
                            var disponibilita = prodotto.LstDisponibilitaProdotto.FirstOrDefault(x => x.FK_Fornitore == importSettings.FK_Fornitore && x.FK_Magazzino == importSettings.FK_Magazzino);

                            if (disponibilita == null)
                            {
                                disponibilita = new Disponibilita();
                                disponibilita.AliquotaIVA = 22;
                                disponibilita.CostoCorriere = 6;
                                disponibilita.FK_Fornitore = importSettings.FK_Fornitore;
                                disponibilita.FK_Magazzino = importSettings.FK_Magazzino;
                                disponibilita.refPneumatico = prodotto;
                                disponibilitaCreate++;
                            }
                            else
                            {
                                disponibilitaAggiornate++;
                            }

                            double prezzo;
                            if (Debugger.IsAttached)
                                prezzo = Convert.ToDouble(tokens[16].Replace(".", ","));
                            else
                                prezzo = Convert.ToDouble(tokens[16].Replace(",", "."));
                            disponibilita.Costo = prezzo;
                            disponibilita.AliquotaIVA = 22;
                            disponibilita.CostoIVA = disponibilita.Costo * (1 + disponibilita.AliquotaIVA / 100);
                            disponibilita.UltimoAggiornamento = DateTime.Now;
                            disponibilita.Quantita = Convert.ToInt32(tokens[15]);
                            disponibilita.CostoCorriereIVA = importSettings.CostoCorriereIVA;
                            disponibilita.CostoCorriere = disponibilita.CostoCorriereIVA * 100 / (100 + disponibilita.AliquotaIVA);
                            disponibilita.MUValoreIVA = importSettings.MarkupEuro;
                            disponibilita.MUValore = importSettings.MarkupEuro * 100 / (100 + disponibilita.AliquotaIVA);
                            disponibilita.MUPerc = importSettings.MarkupPercentuale;
                            disponibilita.CostoIntermediario = importSettings.PercIntermediario;

                            double prezzoTemp = (disponibilita.Costo + disponibilita.CostoCorriere) * (1 + disponibilita.MUPerc / 100) + disponibilita.MUValore;
                            disponibilita.Prezzo = prezzoTemp * 100 / (100 - disponibilita.CostoIntermediario);
                            disponibilita.PrezzoIVA = disponibilita.Prezzo * (1 + disponibilita.AliquotaIVA / 100);
                            disponibilita.UltimoAggiornamento = DateTime.Now;

                            if (disponibilita.Id == 0)
                            {
                                db.DisponibilitaProdotti.Add(disponibilita);
                            }

                            // Aggiunta delle immagini
                            string imgProdotto = tokens[25];
                            if (prodotto.LstImmagini.Count == 0)
                            {
                                ImmaginiProdotto img = new ImmaginiProdotto() { refProdottoPneumatico = prodotto, NomeImg = "ImgDeldo", NomeOriginale = "Immagine DELDO", URLImmagine = tokens[25] };
                                db.ImmaginiProdotti.Add(img);
                            }

                            db.SaveChanges();
                        }
                        numRiga++;
                        Console.WriteLine(numRiga);
                    }

                    importSettings.FileLastUpdate = dataAggiornamentoFile;
                    db.SaveChanges();
                    reader.Close();

                    int dispAzzerate = 0;
                    foreach (var idDeldo in dictProdDeldo.Where(x => x.Value == false))
                    {
                        var prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.IDDeldo == idDeldo.Key);

                        if (prodotto != null)
                        {
                            var disp = prodotto.LstDisponibilitaProdotto.FirstOrDefault(x => x.FK_Fornitore == importSettings.FK_Fornitore && x.FK_Magazzino == importSettings.FK_Magazzino);

                            if (disp != null)
                            {
                                disp.Quantita = 0;
                                dispAzzerate++;
                            }
                        }
                    }

                    string syncResult = string.Format(
                    "Sincronizzazione DELDO ESEGUITA. Prodotti creati: {0} - Marchi creati: {1} - Caratteristiche create: {2}, Disp. create: {3}, Disp. aggiornate: {4}, Disp. azzerate: {5}",
                    prodottiCreati, marchiCreati, caratteristicheProdottoCreate, disponibilitaCreate, disponibilitaAggiornate, dispAzzerate);

                    LogHelper.LogData(LogEntry.DeldoWebJob, syncResult);

                    LogHelper.SetLogStatus(LogEntry.DeldoWebJob, true, false, syncResult);

                    db.SaveChanges();
                }

            }
            catch (Exception ex)
            {
                string errorMessage = string.Format("Errore importazione prodotti DELDO- riga: {1}: {0} ", ex.ToString(), numRiga);

                LogHelper.SetLogStatus(LogEntry.DeldoWebJob, false, false, errorMessage);
                LogHelper.LogData(LogEntry.DeldoWebJob, errorMessage);
                Console.WriteLine(ex.ToString());
            }
        }

        private static void ImportDeldoFilesOld()
        {
            DBModel db = new DBModel();
            var fornitore = db.Fornitore.FirstOrDefault(x => x.Procedura == Fornitore.ProceduraDELDO);

            if (fornitore == null)
            {
                throw new Exception("Impossibile trovare il fornitore con procedura di importazione DELDO associata");
            }

            var magazzino = db.Magazzini.FirstOrDefault(x => x.FK_Fornitore == fornitore.Id);

            if (magazzino == null)
            {
                throw new Exception("Impossibile trovare il magazzino associato al fornitore DELDO");
            }

            var categoria = db.CategorieProdotti.FirstOrDefault();

            foreach (var du in db.DeldoUploads.Where(x => x.DataInizioImportazione == null).ToList())
            {
                du.DataInizioImportazione = TimeHelper.ToItalianTime(DateTime.Now).Value;

                db.SaveChanges();

                Console.Out.WriteLine(string.Format("Parsing in corso del file {0}", du.NomeFile));
                LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Parsing in corso del file {0}", du.NomeFile));

                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(ConfigurationManager.AppSettings["StorageConnectionString"]);
                // Create the blob client.
                CloudBlobClient blobClient = storageAccount.CreateCloudBlobClient();
                // Retrieve reference to a previously created container.
                CloudBlobContainer container = blobClient.GetContainerReference("upload-deldo");
                // Retrieve reference to a blob named "photo1.jpg".
                CloudBlockBlob blockBlob = container.GetBlockBlobReference(du.NomeFile);

                using (var ms = new MemoryStream())
                {
                    blockBlob.DownloadToStream(ms);

                    ms.Seek(0, SeekOrigin.Begin);

                    StreamReader sr = new StreamReader(ms);

                    string riga;
                    string[] tokens;
                    int numRiga = 0;

                    while (!sr.EndOfStream)
                    {
                        try
                        {
                            if (Debugger.IsAttached)
                                Console.WriteLine(numRiga);

                            riga = sr.ReadLine();

                            if (numRiga != 0)
                            {
                                tokens = riga.Split(';');

                                string EAN = tokens[18].Replace("\"", "");
                                string brand = tokens[1];
                                brand = brand.Replace(" ZO", "");
                                brand = brand.Replace(" WI", "");

                                string pattern = tokens[7];

                                string valStagione = string.Empty;
                                switch (tokens[13])
                                {
                                    case "Summer":
                                        valStagione = "ESTIVO";
                                        break;
                                    case "Winter":
                                        valStagione = "INVERNALE";
                                        break;
                                    case "All Season":
                                        valStagione = "ALL SEASON";
                                        break;
                                    default:
                                        //stagione non valida
                                        continue;
                                }

                                string valIndiceCarico = tokens[6].Substring(0, 3);
                                string valIndiceVelocita = tokens[6].Substring(3, 1);
                                valIndiceCarico.Replace("(", "");
                                var strAltezza = tokens[3];
                                var strLarghezza = tokens[2];
                                var strDiametro = tokens[5];

                                if (string.IsNullOrWhiteSpace(valIndiceCarico) ||
                                    string.IsNullOrWhiteSpace(valIndiceVelocita) ||
                                    string.IsNullOrWhiteSpace(strAltezza) ||
                                    string.IsNullOrWhiteSpace(strLarghezza) ||
                                    string.IsNullOrWhiteSpace(strDiametro))
                                {
                                    continue;
                                }



                                double costo = 0;
                                if (Debugger.IsAttached)
                                    costo = Convert.ToDouble(tokens[16].Replace(".", ","));
                                else
                                    costo = Convert.ToDouble(tokens[16].Replace(",", "."));
                                int quantita = Convert.ToInt32(tokens[15]);
                                Prodotto_Pneumatici prodotto;
                                Disponibilita dispProdotto;


                                //Cerco il prodotto su DB in base all'EAN
                                prodotto = db.ProdottiPneumatici.FirstOrDefault(x => x.EAN13 == EAN);

                                if (prodotto == null)
                                {
                                    prodotto = new Prodotto_Pneumatici();
                                    prodotto.DataUltimaModifica = DateTime.Now;
                                    prodotto.Descrizione = string.Format("{0} {1}", brand, pattern);
                                    prodotto.EAN13 = EAN;
                                    prodotto.Modello = pattern;

                                    // Categoria (auto, moto, etc...)
                                    prodotto.FK_Categoria = categoria.Id;

                                    // Marca
                                    var marca = db.Brand.FirstOrDefault(x => x.Nome == brand);
                                    if (marca == null)
                                    {
                                        marca = new Brand() { Nome = brand };
                                        db.Brand.Add(marca);
                                    }
                                    prodotto.refBrand = marca;

                                    // Altezza
                                    var altezza = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Altezza && x.Valore == strAltezza);
                                    if (altezza == null)
                                    {
                                        altezza = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Altezza, Valore = strAltezza };
                                        db.CaratteristicheProdotti.Add(altezza);
                                    }
                                    prodotto.refAltezza = altezza;

                                    var larghezza = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Larghezza && x.Valore == strLarghezza);
                                    if (larghezza == null)
                                    {
                                        larghezza = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Larghezza, Valore = strLarghezza };
                                        db.CaratteristicheProdotti.Add(larghezza);
                                    }
                                    prodotto.refLarghezza = larghezza;

                                    var diametro = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Diametro && x.Valore == strDiametro);
                                    if (diametro == null)
                                    {
                                        diametro = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Diametro, Valore = strDiametro };
                                        db.CaratteristicheProdotti.Add(diametro);
                                    }
                                    prodotto.refDiametro = diametro;

                                    var stagione = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.Stagione && x.Valore == valStagione);
                                    if (stagione == null)
                                    {
                                        stagione = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.Stagione, Valore = valStagione };
                                        db.CaratteristicheProdotti.Add(stagione);
                                    }
                                    prodotto.refStagione = stagione;

                                    var indiceCarico = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceCarico && x.Valore == valIndiceCarico);
                                    if (indiceCarico == null)
                                    {
                                        indiceCarico = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.IndiceCarico, Valore = valIndiceCarico };
                                        db.CaratteristicheProdotti.Add(indiceCarico);
                                    }
                                    prodotto.refIndiceCarico = indiceCarico;

                                    var indiceVelocita = db.CaratteristicheProdotti.FirstOrDefault(x => x.TipoCaratteristica == CaratteristicaProdotto.IndiceVelocita && x.Valore == valIndiceVelocita);
                                    if (indiceVelocita == null)
                                    {
                                        indiceVelocita = new CaratteristicaProdotto() { TipoCaratteristica = CaratteristicaProdotto.IndiceVelocita, Valore = valIndiceVelocita };
                                        db.CaratteristicheProdotti.Add(indiceVelocita);
                                    }
                                    prodotto.refIndiceVelocita = indiceVelocita;
                                    db.ProdottiPneumatici.Add(prodotto);
                                }

                                // Aggiunta della disponibilità
                                dispProdotto = db.DisponibilitaProdotti.FirstOrDefault(x => x.FK_Fornitore == fornitore.Id && x.FK_ProdottoPneumatici == prodotto.Id && x.FK_Magazzino == magazzino.Id);
                                if (dispProdotto != null)
                                {
                                    //Prodotto già esistente, aggiorno soltanto quantità e prezzo
                                    dispProdotto.AliquotaIVA = du.AliquotaIVA;
                                    dispProdotto.Costo = costo;
                                    dispProdotto.MUPerc = du.MUPerc;
                                    dispProdotto.MUValore = du.MUValore;
                                    dispProdotto.Prezzo = costo + du.MUValore + (costo * du.MUPerc / 100);
                                    dispProdotto.PrezzoIVA = dispProdotto.Prezzo + (dispProdotto.Prezzo * du.AliquotaIVA / 100);
                                    dispProdotto.Quantita = quantita;
                                    dispProdotto.UltimoAggiornamento = DateTime.Now;
                                }
                                else
                                {
                                    dispProdotto = new Disponibilita();
                                    dispProdotto.FK_Fornitore = fornitore.Id;
                                    dispProdotto.FK_Magazzino = magazzino.Id;
                                    dispProdotto.refPneumatico = prodotto;
                                    dispProdotto.AliquotaIVA = du.AliquotaIVA;
                                    dispProdotto.Costo = costo;
                                    dispProdotto.MUPerc = du.MUPerc;
                                    dispProdotto.MUValore = du.MUValore;
                                    dispProdotto.Prezzo = costo + du.MUValore + (costo * du.MUPerc / 100);
                                    dispProdotto.PrezzoIVA = dispProdotto.Prezzo + (dispProdotto.Prezzo * du.AliquotaIVA / 100);
                                    dispProdotto.Quantita = quantita;
                                    dispProdotto.UltimoAggiornamento = DateTime.Now;
                                    db.DisponibilitaProdotti.Add(dispProdotto);
                                }

                                // Aggiunta delle immagini
                                string imgProdotto = tokens[25];
                                if (prodotto.LstImmagini.Count == 0)
                                {
                                    ImmaginiProdotto img = new ImmaginiProdotto() { refProdottoPneumatico = prodotto, NomeImg = "ImgDeldo", NomeOriginale = "Immagine DELDO", URLImmagine = tokens[25] };
                                    db.ImmaginiProdotti.Add(img);
                                }

                                db.SaveChanges();
                            }

                        }
                        catch (Exception ex)
                        {
                            LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Errore parsing del file {0} alla riga {2}: {1}", du.NomeFile, ex.ToString(), numRiga));
                        }
                        numRiga++;
                    }

                    du.DataFineImportazione = TimeHelper.ToItalianTime(DateTime.Now).Value;

                    db.SaveChanges();
                    LogHelper.LogData(LogEntry.DeldoWebJob, string.Format("Parsing del file {0} completato correttamente", du.NomeFile));
                    Console.Out.WriteLine("Parsing del file {0} completato correttamente", du.NomeFile);
                }
            }
        }
    }
}
