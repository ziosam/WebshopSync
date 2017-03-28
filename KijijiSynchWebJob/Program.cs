using FS.SpareStores.DAL.Helpers;
using FS.SpareStores.DAL.Model;
using FS.SpareStores.Helpers;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace KijijiSynchWebJob {
    class Program {
        static void Main(string[] args) {
            while (true)
            {
                try
                {
                    SynchProdotti();
                }
                catch (Exception ex)
                {
                    LogHelper.LogData(LogEntry.KijijiWebJob, $"Errore sincronizzazione Kijiji: {ex.ToString()}");
                }

                Thread.Sleep(Convert.ToInt32(new TimeSpan(4, 0, 0).TotalMilliseconds));
            }
        }

        private static void SynchProdotti() {
            LogHelper.LogData(LogEntry.PrestashopWebJob, string.Format("Creazione CSV per KIJIJI"));
            var stream = new MemoryStream();
            var csvWriter = new StreamWriter(stream, Encoding.UTF8);

            const string categoriaPnemuaticiCerchiAuto = "587399168";
            const string categoriaPneumaticiCerchiMoto = "587595776";
            const string email = "customer.spares@gmail.com";
            const string telefono = "3928545490";
            const string tipoPrezzo = "SPECIFIED_AMOUNT";
            const string istatComune = "001308";
            var url = string.Empty;

            using (var db = new DBModel()) {
                foreach (var ps in db.PS_Shop.Where(x => x.Disabled == false).ToList()) {
                    string azione = "inserisci", Titolo, Descrizione, Prezzo, dataPubblicazione;
                    string urlImg1 = string.Empty, urlImg2 = string.Empty, urlImg3 = string.Empty, urlImg4 = string.Empty;
                    string urlImg5 = string.Empty, urlImg6 = string.Empty, urlImg7 = string.Empty, urlImg8 = string.Empty;
                    string gommeUsate, descrizioneBreve;

                    foreach (var shopSync in ps.LstMagazziniSincronizzati.Where(x => x.SincronizzazioneAttiva).ToList()) {
                        List<int> lstBrandDaSincronizzare = new List<int>();
                        List<int> lstCaratteristicheDaSincronizzare = new List<int>();

                        if (shopSync.SincronizzazioneSelettiva)
                        {
                            LogHelper.LogData(LogEntry.KijijiWebJob,
                                $"Sincronizzazione attivata per il magazzino: {shopSync.refMagazzino.Nome}. Carico le liste di marchi e caratteristiche da sincronizzare");
                            lstBrandDaSincronizzare = shopSync.LstSelectiveBrandSynch.Select(x => x.FK_Brand).ToList();
                            lstCaratteristicheDaSincronizzare = shopSync.LstSelectiveCarSync.Select(x => x.FK_ValoreCaratteristica).ToList();
                        }

                        foreach (var disp in shopSync.refMagazzino.LstDisponibilitaProdotti.Where(x => (x.Quantita - x.LstVendita.Sum(y => y.Quantita)) > 0)) {
                            if (shopSync.SincronizzazioneSelettiva)
                            {
                                if (!lstBrandDaSincronizzare.Contains(disp.refPneumatico.FK_Brand))
                                {
                                    LogHelper.LogData(LogEntry.KijijiWebJob,
                                        $"Sincronizzazione attivata prodotto {disp.Id} escluso per marchio non da sincronizzare.");

                                    continue;
                                }

                                if (!lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Altezza) ||
                                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Diametro) ||
                                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_IndiceCarico) ||
                                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_IndiceVelocita) ||
                                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Larghezza) ||
                                    (disp.refPneumatico.FK_MandS.HasValue && !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_MandS.Value)) ||
                                    !lstCaratteristicheDaSincronizzare.Contains(disp.refPneumatico.FK_Stagione))
                                {
                                    LogHelper.LogData(LogEntry.KijijiWebJob,
                                        $"Sincronizzazione attivata prodotto {disp.Id} escluso per caratteristica non da sincronizzare.");
                                    continue;
                                }
                            }

                            var partnerId = disp.Id.ToString();

                            if (disp.LstSyncPrestashop.Any()) {
                                url =
                                    $"https://www.gommeusatestore.com/index.php?controller=product&id_product={disp.LstSyncPrestashop.Last().IdPrestashop}";
                            } else {
                                url = string.Empty;
                            }

                            // Titolo (nome prodotto prestashop)
                            if (disp.refPneumatico.Usato) {
                                gommeUsate = $"Gomme usate - Batt. {disp.refPneumatico.PercResidua}%";
                                descrizioneBreve = "GLI PNEUMATICI HANNO SUPERATO TUTTI I NOSTRI TEST DI SICUREZZA E SONO STATI TRATTATI CON UNA SPECIALE CERA CHE NE RINNOVA E PROTEGGE L’ELASTICITA’ PREVENENDO LA SCREPOLATURA E L’INVECCHIAMENTO.";
                            } else {
                                gommeUsate = "Gomme";
                                descrizioneBreve = "GARANZIA: 2 ANNI (SUI DIFETTI DI FABBRICA)";
                            }

                            Titolo = string.Format("{8} {0} {1}/{2}R{3} {4}{5} {6} {7}", disp.refPneumatico.refBrand.Nome, disp.refPneumatico.refLarghezza.Valore, disp.refPneumatico.refAltezza.Valore,
                                disp.refPneumatico.refDiametro.Valore, disp.refPneumatico.refIndiceCarico.Valore, disp.refPneumatico.refIndiceVelocita.Valore, disp.refPneumatico.Modello,
                                disp.refPneumatico.refStagione.Valore, gommeUsate);

                            Console.WriteLine(Titolo);

                            Descrizione = "MINIMO D'ORDINE: 2 PNEUMATICI<br />";

                            if (disp.refPneumatico.refLarghezza != null)
                                Descrizione += $"Larghezza: {disp.refPneumatico.refLarghezza.Valore}<br />";

                            if (disp.refPneumatico.refAltezza != null)
                                Descrizione += $"Altezza: {disp.refPneumatico.refAltezza.Valore}<br />";

                            if (disp.refPneumatico.refDiametro != null)
                                Descrizione += $"Diametro: {disp.refPneumatico.refDiametro.Valore}<br />";

                            if (disp.refPneumatico.refIndiceCarico != null)
                                Descrizione += $"Ind. carico: {disp.refPneumatico.refIndiceCarico.Valore}<br />";

                            if (disp.refPneumatico.refIndiceVelocita != null)
                                Descrizione += $"Ind. velocita': {disp.refPneumatico.refIndiceVelocita.Valore}<br />";

                            if (disp.refPneumatico.refBrand != null)
                                Descrizione += $"Marca: {disp.refPneumatico.refBrand.Nome}<br />";

                            Descrizione += $"Modello: {disp.refPneumatico.Modello}<br />";

                            Descrizione += $"Condizione: {(disp.refPneumatico.Usato ? "Usato" : "Nuovo")}<br />";

                            if(disp.refPneumatico.Usato)
                                Descrizione += $"Batt. residuo: {disp.refPneumatico.PercResidua.Value}<br />";

                            if (disp.refPneumatico.refStagione != null)
                                Descrizione += $"Stagione: {disp.refPneumatico.refStagione.Valore}<br />";


                            Descrizione += $"Runflat: {(disp.refPneumatico.Runflat ? "si" : "no")}<br />";

                            if (disp.refPneumatico.refMudAndSnow != null)
                                Descrizione += $"M+S: {disp.refPneumatico.refMudAndSnow.Valore}<br />";

                            if (string.IsNullOrEmpty(disp.refPneumatico.DOT)) {
                            } else {
                                disp.refPneumatico.DOT = disp.refPneumatico.DOT.PadLeft(4, '0');
                                Descrizione +=
                                    $"DOT: Settimana {disp.refPneumatico.DOT.Substring(0, 2)} Anno 20{disp.refPneumatico.DOT.Substring(2, 2)}<br />";
                            }

                            Descrizione += $"BSW: {(disp.refPneumatico.Bordino ? "si" : "no")}<br />";
                            Descrizione += $"XL: {(disp.refPneumatico.Bordino ? "si" : "no")}<br /><br />";

                            // Aggiunta descrizione Marca
                            Descrizione += $"{disp.refPneumatico.refBrand.Descrizione}<br /><br />";

                            // Aggiunta Descrizione breve
                            Descrizione += $"{descrizioneBreve}<br />";

                            Descrizione = Descrizione.Replace(';', ',').Replace(System.Environment.NewLine, "<br />");
                            Prezzo = Math.Floor(disp.PrezzoIVA * (1 + shopSync.MarkupPercentuale / 100) + shopSync.MarkupValore).ToString();
                            dataPubblicazione = disp.UltimoAggiornamento.ToString("dd-MM-yyyy hh:mm:ss");
                            //dataPubblicazione = DateTime.Now.ToString("dd-MM-yyyy hh:mm:ss");

                            int countImg = 1;
                            foreach (var img in disp.refPneumatico.LstImmagini) {
                                switch (countImg) {
                                    case 1:
                                        urlImg1 = img.URLImmagine;
                                        break;
                                    case 2:
                                        urlImg2 = img.URLImmagine;
                                        break;
                                    case 3:
                                        urlImg3 = img.URLImmagine;
                                        break;
                                    case 4:
                                        urlImg4 = img.URLImmagine;
                                        break;
                                    case 5:
                                        urlImg5 = img.URLImmagine;
                                        break;
                                    case 6:
                                        urlImg6 = img.URLImmagine;
                                        break;
                                    case 7:
                                        urlImg7 = img.URLImmagine;
                                        break;
                                    case 8:
                                        urlImg8 = img.URLImmagine;
                                        break;
                                    default:
                                        break;
                                }
                            }

                            csvWriter.WriteLine("{0};{1};{2};{3};{4};{5};{6};{7};{8};{9};{10};{11};{12};{13};{14};{15};{16};{17};{18};{19}",
                                partnerId,
                                azione,
                                Titolo,
                                Descrizione,
                                email,
                                url,
                                telefono,
                                Prezzo,
                                tipoPrezzo,
                                istatComune,
                                categoriaPnemuaticiCerchiAuto,
                                dataPubblicazione,
                                urlImg1,
                                urlImg2,
                                urlImg3,
                                urlImg4,
                                urlImg5,
                                urlImg6,
                                urlImg7,
                                urlImg8);
                        }
                    }
                }
            }

            csvWriter.Flush();

            // Writing to file (for test)
            //string filename = AppDomain.CurrentDomain.BaseDirectory + "\\TestSpares_Kijiji.csv";

            //FileStream fs = File.Create(filename);
            //stream.Position = 0;
            //stream.CopyTo(fs);
            //fs.Flush();

            // Upload to Azure Blob
            BlobAzureHelper myBlobHelper = new BlobAzureHelper();
            if (!myBlobHelper.IsInitialized) {
                Console.WriteLine("Errore connessione blob storage");
            } else {
                string uploadedFileName = "spares-products.csv";

                //Caricamento su Blob Azure
                stream.Position = 0;
                myBlobHelper.UploadStream(stream, BlobAzureHelper.ContainerUploadKijiji, uploadedFileName, "text/csv");
            }

            csvWriter.Close();
            stream.Close();
            //fs.Close();

            //Console.WriteLine(string.Format("File generato: {0}", filename));
            //Console.ReadKey();
        }
    }
}
