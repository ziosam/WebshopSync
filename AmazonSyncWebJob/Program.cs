using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Azure.WebJobs;
using FS.SpareStores.DAL.Model;
using FS.SpareStores.DAL.XMLHelperClass;
using FS.SpareStores.DAL.Helpers;
using System.Threading;
using System.Data.SqlClient;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.IO;

namespace AmazonSyncWebJob {
    // To learn more about Microsoft Azure WebJobs SDK, please see http://go.microsoft.com/fwlink/?LinkID=320976
    class Program {
        static DBModel db;

        static List<Product> amazonProducts = new List<Product>();
        static List<Inventory> amazonInventory = new List<Inventory>();
        static List<Price> amazonPrices = new List<Price>();
        static List<ProductImage> amazonProductImages = new List<ProductImage>();

        const int maxPerLista = 2000;

        // Please set the following connection strings in app.config for this WebJob to run:
        // AzureWebJobsDashboard and AzureWebJobsStorage
        static void Main() {
            while (true) {
                try {
                    EseguiProcessiAmazon();
                }
                catch (Exception ex) {
                    LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("ERRORE PROCESSO SYNC AMAZON: {0}. ", ex.ToString()));
                }
                Thread.Sleep(new TimeSpan(1, 0, 0));
            }
        }

        private static void AggiornaCambi() {
            try {
                Dictionary<string, MagazzinoSyncMarketplace> lstValuteMarketplace = new Dictionary<string, MagazzinoSyncMarketplace>();
                DateTime dataRichiesta = DateTime.Now.Date.AddDays(-1);
                int numRiga = 0;

                foreach (var m in db.MagazziniSyncMarketplaces.Where(x => x.AggiornaCambioAutomaticamente == true && x.DataAggiornamentoCambio < dataRichiesta)) {
                    lstValuteMarketplace.Add(m.SiglaValuta, m);
                }

                if (lstValuteMarketplace.Count > 0) {
                    string requestURL = string.Format(
                        "http://cambi.bancaditalia.it/cambi/QueryOneDateAllCur?lang=ita&rate=0&initDay={0}&initMonth={1}&initYear={2}&refCur=euro&R1=csv",
                        dataRichiesta.Day.ToString().PadLeft(2, '0'),
                        dataRichiesta.Month.ToString().PadLeft(2, '0'),
                        dataRichiesta.Year);
                    using (WebClient client = new WebClient()) {
                        using (MemoryStream ms = new MemoryStream(client.DownloadData(requestURL))) {
                            ms.Seek(0, SeekOrigin.Begin);
                            StreamReader reader = new StreamReader(ms);

                            while (!reader.EndOfStream) {
                                string riga = reader.ReadLine();
                                if (numRiga > 3) {
                                    var tokens = riga.Split(',');

                                    if (lstValuteMarketplace.ContainsKey(tokens[2])) {
                                        if (Debugger.IsAttached) {
                                            lstValuteMarketplace[tokens[2]].Cambio = Convert.ToDouble(tokens[4].Replace('.', ','));
                                        } else {
                                            lstValuteMarketplace[tokens[2]].Cambio = Convert.ToDouble(tokens[4]);
                                        }
                                        lstValuteMarketplace[tokens[2]].DataAggiornamentoCambio = dataRichiesta;
                                        LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Cambio per la valuta {0} aggiornato a {1}", lstValuteMarketplace[tokens[2]].SiglaValuta, lstValuteMarketplace[tokens[2]].Cambio));
                                    }
                                }
                                numRiga++;
                            }

                            reader.Close();
                            reader.Dispose();
                        }

                        client.Dispose();
                    }
                    db.SaveChanges();
                }
            }
            catch (Exception ex) {
                LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Errore sincronizzazione cambi {0}", ex.ToString()));
            }
        }

        private static void EseguiProcessiAmazon() {
            try {
                while (true) {
                    LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, true, "Sincronizzazione in corso");

                    db = new DBModel();
                    AggiornaCambi();

                    foreach (var mpl in db.MagazziniSyncMarketplaces.ToList()) {
                        mpl.NumErroriSincronizzazione = 0;
                        mpl.NumProdottiSincronizzati = 0;
                        //LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Gestione caricamento magazzino {0} - Marketplace {1}", mpl.refMagazzino.Nome, mpl.refMarketplace.Descrizione));

                        if (mpl.SincronizzazioneAttiva == false && mpl.AzzeratoSuAmazon == false) {
                            // Marketplace non più da sincronizzare, azzero i prodotti
                            RimuoviProdottiNonPresenti(mpl);
                        } else {
                            Console.WriteLine("Sincronizzazione Marketplace {0}", mpl.refMarketplace.Descrizione);
                            mpl.InizioUltimaSincronizzazione = DateTime.Now;
                            SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["DBModel"].ConnectionString);
                            SqlCommand cmd = new SqlCommand("GetDatiDaSincronizzareDeldo", conn);
                            cmd.CommandTimeout = 36000;
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.Add(new SqlParameter("@magazziniSyncMarketplaceId", mpl.Id));

                            var adapt = new SqlDataAdapter();
                            adapt.SelectCommand = cmd;
                            var dataset = new DataSet();
                            adapt.Fill(dataset);
                            conn.Close();

                            var datList = dataset.Tables[0].AsEnumerable().ToList();
                            int tot = datList.Count;
                            int i = 1;

                            List<int> fakeListInt = new List<int>();
                            fakeListInt.Add(19179);

                            foreach (var dr in datList) {
                                //foreach (var dr in fakeListInt) {
                                var dp = db.DisponibilitaProdotti.Find(dr[0]);
                                //var dp = db.DisponibilitaProdotti.Find(dr);
                                if (dp.Id == 19179)
                                    Console.WriteLine("lei");

                                double costoTarget = 0;
                                FasciaRicarico fascia;

                                try {
                                    Console.WriteLine(string.Format("{0}/{1} Sincronizzazione {2}", i, tot, dp.refPneumatico.Descrizione));
                                    i++;
                                    if (dp.refPneumatico.EAN13 == "3286340712316") {
                                        Console.WriteLine("ESCLUSO!");
                                        //EAN escluso per problema Amazon
                                        continue;
                                    }

                                    if (dp.refPneumatico.EAN13 == "") {
                                        Console.WriteLine("Check!");
                                        continue;
                                    }

                                    var sync = dp.LstSyncAmazon.FirstOrDefault(x => x.FK_MagazzinoSyncMarketplace == mpl.Id);

                                    if (sync == null) {
                                        sync = new DisponibilitaSyncMarketplace();

                                        sync.DataInizioSincronizzazione = DateTime.Now;
                                        sync.DataFineSincronizzazione = new DateTime(1983, 09, 05);
                                        sync.refDisponibilita = dp;
                                        sync.refMagazzinoSyncMarketplace = mpl;
                                        sync.Quantita = dp.Quantita;
                                        sync.Costo = dp.Costo;
                                        sync.StatoSincronizzazione = (int)StatoCaricamentoDisponibilità.NonSincronizzata;
                                        // Aggiungo IVA
                                        sync.Prezzo = sync.Costo * (1 + dp.AliquotaIVA / 100);
                                        // Aggiungo MU

                                        costoTarget = Math.Ceiling(sync.Prezzo);
                                        fascia = mpl.LstFasceRicarico.FirstOrDefault(x => x.DaPrezzo <= costoTarget && x.APrezzo >= costoTarget);
                                        if (fascia == null) {
                                            LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Impossibile trovare una fascia di prezzo. Costo ricercato: {0}", sync.Costo));
                                            continue;
                                            // Questo non lo posso sincronizzare, vado al successivo
                                        }
                                        sync.Prezzo = sync.Prezzo * (1 + fascia.RicaricoPerc / 100);
                                        // Aggiunta commissione Amazon
                                        sync.Prezzo = sync.Prezzo * 100 / (100 - mpl.MargineIntermediario);
                                        // Cambio
                                        if (mpl.Cambio != 0) {
                                            sync.Prezzo = sync.Prezzo * mpl.Cambio;
                                        }

                                        db.DispSyncMarketplace.Add(sync);
                                    }

                                    // Provo a mandare sempre tutto
                                    // Invio dati prodotti
                                    Product p = new Product();
                                    if (dp.refPneumatico.Usato) {
                                        p.Condition = new ConditionInfo() { ConditionType = ConditionType.UsedVeryGood };
                                    } else {
                                        p.Condition = new ConditionInfo() { ConditionType = ConditionType.New };
                                    }
                                    p.SKU = "SPARES" + dp.Id.ToString();
                                    p.DescriptionData = new ProductDescriptionData();
                                    p.DescriptionData.Brand = dp.refPneumatico.refBrand.Nome;
                                    p.DescriptionData.Description = dp.refPneumatico.Descrizione;
                                    p.DescriptionData.Title = dp.refPneumatico.Descrizione;

                                    p.DescriptionData.ItemDimensions = new Dimensions();
                                    p.DescriptionData.ItemDimensions.Length = new LengthDimension() { unitOfMeasure = LengthUnitOfMeasure.CM, Value = 70 };
                                    p.DescriptionData.ItemDimensions.Height = new LengthDimension() { unitOfMeasure = LengthUnitOfMeasure.CM, Value = 28 };
                                    p.DescriptionData.ItemDimensions.Weight = new WeightDimension() { unitOfMeasure = WeightUnitOfMeasure.KG, Value = 9 };
                                    p.DescriptionData.ItemDimensions.Width = new LengthDimension() { unitOfMeasure = LengthUnitOfMeasure.CM, Value = 70 };

                                    p.StandardProductID = new StandardProductID() { Type = StandardProductIDType.EAN, Value = dp.refPneumatico.EAN13 };
                                    amazonProducts.Add(p);
                                    sync.StatoSincronizzazione = (int)StatoCaricamentoDisponibilità.InviatoDatiProdotto;

                                    // Invio dati quantità
                                    Inventory inv = new Inventory();
                                    inv.SKU = "SPARES" + dp.Id.ToString();
                                    inv.RestockDateSpecified = false;
                                    if (dp.Quantita > mpl.MinQuantity)
                                        inv.Quantity = dp.Quantita.ToString();
                                    else
                                        inv.Quantity = "0";
                                    amazonInventory.Add(inv);
                                    sync.StatoSincronizzazione = (int)StatoCaricamentoDisponibilità.InviatoDatiQuantita;

                                    // Forzo il ricalcolo dei prezzi, nel caso in cui siano cambiati
                                    sync.Quantita = dp.Quantita;
                                    sync.Costo = dp.Costo;
                                    sync.Prezzo = sync.Costo * (1 + dp.AliquotaIVA / 100);
                                    costoTarget = Math.Ceiling(sync.Prezzo);
                                    fascia = mpl.LstFasceRicarico.FirstOrDefault(x => x.DaPrezzo <= costoTarget && x.APrezzo >= costoTarget);
                                    if (fascia == null) {
                                        LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Impossibile trovare una fascia di prezzo. Costo ricercato: {0}", sync.Costo));
                                        continue;
                                        // Questo non lo posso sincronizzare, vado al successivo
                                    }
                                    sync.Prezzo = sync.Prezzo * (1 + fascia.RicaricoPerc / 100);
                                    // Aggiunta commissione Amazon
                                    sync.Prezzo = sync.Prezzo * 100 / (100 - mpl.MargineIntermediario);
                                    // Cambio
                                    if (mpl.Cambio != 0) {
                                        sync.Prezzo = sync.Prezzo * mpl.Cambio;
                                    }

                                    // Aggiungo dati prezzo
                                    Price price = new Price();
                                    price.SKU = "SPARES" + dp.Id.ToString();

                                    price.StandardPrice = new OverrideCurrencyAmount() { currency = BaseCurrencyCodeWithDefault.EUR, Value = Convert.ToDecimal(Math.Ceiling(sync.Prezzo)), zeroSpecified = false };

                                    //Se nel caricamento è stato specificato il cambio allora la valuta è la sterlina
                                    if (mpl.Cambio != 0) {
                                        price.StandardPrice.currency = BaseCurrencyCodeWithDefault.GBP;
                                    }
                                    amazonPrices.Add(price);
                                    sync.StatoSincronizzazione = (int)StatoCaricamentoDisponibilità.SincronizzazioneCompletata;
                                    sync.DataFineSincronizzazione = DateTime.Now;
                                }
                                catch (Exception ex) {
                                    string errorMessage = string.Format("Errore caricamento prodotto con EAN {0}. Dettaglio errore: {1}", dp.refPneumatico.EAN13, ex.ToString());

                                    LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, false, errorMessage);
                                    LogHelper.LogData(LogEntry.AmazonWebJob, errorMessage);
                                    mpl.NumErroriSincronizzazione++;
                                }
                            }
                            mpl.FineUltimaSincronizzazione = DateTime.Now;


                            // Ora procedo all'invio vero e proprio
                            if (amazonProducts.Count > 0) {
                                Console.WriteLine(string.Format("Marketplace: {0} - Invio dati prodotti: {1}", mpl.refMarketplace.Descrizione, amazonProducts.Count));
                                LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Marketplace: {0} - Invio dati prodotti: {1}", mpl.refMarketplace.Descrizione, amazonProducts.Count));
                                InviaDatiProdotti(mpl);
                            } else {
                                Console.Out.WriteLine("Non ci sono dati prodotti da inviare");
                            }

                            if (amazonInventory.Count > 0) {
                                Console.WriteLine(string.Format("Marketplace: {0} - Invio dati quantita: {1}", mpl.refMarketplace.Descrizione, amazonInventory.Count));
                                LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Marketplace: {0} - Invio dati quantita: {1}", mpl.refMarketplace.Descrizione, amazonInventory.Count));
                                InviaDatiQuantita(mpl, amazonInventory, "Invio feed quantita'");
                            } else {
                                Console.Out.WriteLine("Non ci sono dati di quantita' da inviare");
                            }

                            if (amazonPrices.Count > 0) {
                                Console.WriteLine(string.Format("Marketplace: {0} - Invio dati prezzo: {1}", mpl.refMarketplace.Descrizione, amazonPrices.Count));
                                LogHelper.LogData(LogEntry.AmazonWebJob, string.Format("Marketplace: {0} - Invio dati prezzo: {1}", mpl.refMarketplace.Descrizione, amazonPrices.Count));
                                InviaDatiPrezzo(mpl);
                            } else {
                                Console.Out.WriteLine("Non ci sono dati di prezzo da inviare");
                            }

                            amazonProductImages.Clear();
                            amazonProducts.Clear();
                            amazonPrices.Clear();
                            amazonInventory.Clear();

                            db.SaveChanges();
                            Thread.Sleep(6000);
                            Console.WriteLine("Primo ciclo finito");
                        }
                    }

                    LogHelper.SetLogStatus(LogEntry.AmazonWebJob, true, false, "Sincronizzazione completata");
                }
            }
            catch (Exception ex) {
                System.Diagnostics.Trace.TraceError("Errore nel processare gli invii ad amazon. Errore: {0}", ex.Message);
                LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, false, string.Format("Errore nel processare gli invii ad amazon. Errore: {0}", ex.Message));
            }
        }

        private static void RimuoviProdottiNonPresenti(MagazzinoSyncMarketplace c) {
            try {
                //SqlConnection conn = new SqlConnection(System.Configuration.ConfigurationManager.ConnectionStrings["DBModel"].ConnectionString);
                //SqlCommand cmd = new SqlCommand("GetProdottiInsesistenti", conn);
                //cmd.CommandTimeout = 36000;
                //cmd.CommandType = CommandType.StoredProcedure;
                //cmd.Parameters.Add(new SqlParameter("@idUltimoInserimento", c.Id));
                //cmd.Parameters.Add(new SqlParameter("@idMarketplace", c.FK_MarketPlace));

                //var adapt = new SqlDataAdapter();
                //adapt.SelectCommand = cmd;
                //var dataset = new DataSet();
                //adapt.Fill(dataset);
                //conn.Close();

                //var datList = dataset.Tables[0].AsEnumerable().ToList();

                //List<Inventory> lstQuantitaReset = new List<Inventory>();

                //foreach (var r in datList)
                //{
                //    Inventory inv = new Inventory();

                //    inv.SKU = r[0].ToString();
                //    inv.RestockDateSpecified = false;

                //    inv.Quantity = "0";

                //    lstQuantitaReset.Add(inv);
                //}

                //if (lstQuantitaReset.Count > 0)
                //{
                //    Console.Out.WriteLine(string.Format("Portata a 0 la quantita' per {0} prodotti", lstQuantitaReset.Count));
                //    LogHelper.LogData("Processo caricamento", string.Format("Portata a 0 la quantita' per {0} prodotti", lstQuantitaReset.Count));
                //    InviaDatiQuantita(c, lstQuantitaReset, "Invio feed quantita'");
                //}
                //else
                //{
                //    Console.Out.WriteLine("Non ci sono prodotti da rimuovere.");
                //    LogHelper.LogData("Processo caricamento", string.Format("Non ci sono prodotti da rimuovere."));
                //}

            }
            catch (Exception ex) {
                Console.Error.WriteLine("Errore rimozione prodotti non più presenti nell'ultimo file caricato " + ex.ToString());
            }

        }

        private static void InviaDatiProdotti(MagazzinoSyncMarketplace msm) {
            try {
                var masterSendList = new List<List<object>>();
                int count = 0;

                var newSendList = new List<object>();
                foreach (var amazonProductUpdate in amazonProducts) {
                    newSendList.Add(amazonProductUpdate);
                    count++;
                    if (newSendList.Count == maxPerLista) {
                        masterSendList.Add(newSendList);
                        newSendList = new List<object>();
                    }
                }
                if (newSendList.Count > 0) masterSendList.Add(newSendList);

                var submissionIds = FeedSender.SendAmazonFeeds(masterSendList, AmazonEnvelopeMessageType.Product, AmazonFeedType._POST_PRODUCT_DATA_, msm.refMarketplace.MarketplaceId);

                int numParte = 1;

                foreach (var sId in submissionIds) {
                    InvioFeed invioFeed = new InvioFeed() {
                        refSyncMarketplace = msm,
                        DescrizioneFeed = string.Format("Invio feed prodotti - parte {0}", numParte),
                        FeedReportAggiornato = false,
                        FeedSubmissionId = sId,
                        RigheProcessateAmazon = 0,
                        RigheProcessateConErrori = 0,
                        RigheProcessateConWarning = 0,
                        RigheProcessateCorrettamente = 0
                    };

                    db.InviiFeedAmazon.Add(invioFeed);
                    numParte++;
                }

            }
            catch (Exception ex) {
                Console.Error.WriteLine("Errore invio lista prodotti. Errore: {0}", ex);
                System.Diagnostics.Trace.TraceError("Errore invio lista prodotti. Errore: {0}", ex.Message);
                LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, false, string.Format("Errore invio lista prodotti. Errore: {0}", ex));
            }
        }

        private static void InviaDatiQuantita(MagazzinoSyncMarketplace msm, List<Inventory> listaInvioQuantita, string descrizioneInvio) {
            try {
                var masterSendList = new List<List<object>>();
                int count = 0;

                var newSendList = new List<object>();
                foreach (var inv in listaInvioQuantita) {
                    newSendList.Add(inv);
                    count++;
                    if (newSendList.Count == maxPerLista) {
                        masterSendList.Add(newSendList);
                        newSendList = new List<object>();
                    }
                }
                if (newSendList.Count > 0) masterSendList.Add(newSendList);

                var submissionIds = FeedSender.SendAmazonFeeds(masterSendList, AmazonEnvelopeMessageType.Product, AmazonFeedType._POST_INVENTORY_AVAILABILITY_DATA_, msm.refMarketplace.MarketplaceId);

                int numParte = 1;

                foreach (var sId in submissionIds) {
                    InvioFeed invioFeed = new InvioFeed() {
                        DescrizioneFeed = string.Format("{0} - parte {1}", descrizioneInvio, numParte),
                        FeedReportAggiornato = false,
                        FeedSubmissionId = sId,
                        refSyncMarketplace = msm,
                        RigheProcessateAmazon = 0,
                        RigheProcessateConErrori = 0,
                        RigheProcessateConWarning = 0,
                        RigheProcessateCorrettamente = 0
                    };

                    db.InviiFeedAmazon.Add(invioFeed);
                    numParte++;
                }
            }
            catch (Exception ex) {
                LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, false, string.Format("Errore invio lista prodotti. Errore: {0}", ex));
                Console.Error.WriteLine("Errore invio lista quantità. Errore: {0}", ex);
                System.Diagnostics.Trace.TraceError("Errore invio lista quantità. Errore: {0}", ex.Message);
            }
        }

        private static void InviaDatiPrezzo(MagazzinoSyncMarketplace msm) {
            try {
                var masterSendList = new List<List<object>>();
                int count = 0;

                var newSendList = new List<object>();
                foreach (var p in amazonPrices) {
                    newSendList.Add(p);
                    count++;
                    if (newSendList.Count == maxPerLista) {
                        masterSendList.Add(newSendList);
                        newSendList = new List<object>();
                    }
                }
                if (newSendList.Count > 0) masterSendList.Add(newSendList);

                var submissionIds = FeedSender.SendAmazonFeeds(masterSendList, AmazonEnvelopeMessageType.Product, AmazonFeedType._POST_PRODUCT_PRICING_DATA_, msm.refMarketplace.MarketplaceId);

                int numParte = 1;

                foreach (var sId in submissionIds) {
                    InvioFeed invioFeed = new InvioFeed() {
                        DescrizioneFeed = string.Format("Invio feed prezzo - parte {0}", numParte),
                        FeedReportAggiornato = false,
                        FeedSubmissionId = sId,
                        refSyncMarketplace = msm,
                        RigheProcessateAmazon = 0,
                        RigheProcessateConErrori = 0,
                        RigheProcessateConWarning = 0,
                        RigheProcessateCorrettamente = 0
                    };

                    db.InviiFeedAmazon.Add(invioFeed);
                    numParte++;
                }
            }
            catch (Exception ex) {
                LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, false, string.Format("Errore invio lista prodotti. Errore: {0}", ex));
                Console.Error.WriteLine("Errore invio lista prezzi. Errore: {0}", ex);
                System.Diagnostics.Trace.TraceError("Errore invio lista prezzi. Errore: {0}", ex.Message);
            }
        }

        private static void InviaDatiImmagini(MagazzinoSyncMarketplace msm) {
            try {
                var masterSendList = new List<List<object>>();
                int count = 0;

                var newSendList = new List<object>();
                foreach (var p in amazonProductImages) {
                    newSendList.Add(p);
                    count++;
                    if (newSendList.Count == maxPerLista) {
                        masterSendList.Add(newSendList);
                        newSendList = new List<object>();
                    }
                }
                if (newSendList.Count > 0) masterSendList.Add(newSendList);

                var submissionIds = FeedSender.SendAmazonFeeds(masterSendList, AmazonEnvelopeMessageType.Product, AmazonFeedType._POST_PRODUCT_IMAGE_DATA_, msm.refMarketplace.MarketplaceId);

                int numParte = 1;

                foreach (var sId in submissionIds) {
                    InvioFeed invioFeed = new InvioFeed() {
                        DescrizioneFeed = string.Format("Invio feed immagini - parte {0}", numParte),
                        FeedReportAggiornato = false,
                        FeedSubmissionId = sId,
                        refSyncMarketplace = msm,
                        RigheProcessateAmazon = 0,
                        RigheProcessateConErrori = 0,
                        RigheProcessateConWarning = 0,
                        RigheProcessateCorrettamente = 0
                    };

                    db.InviiFeedAmazon.Add(invioFeed);
                    numParte++;
                }
            }
            catch (Exception ex) {
                LogHelper.SetLogStatus(LogEntry.AmazonWebJob, false, false, string.Format("Errore invio lista prodotti. Errore: {0}", ex));
                Console.Error.WriteLine("Errore invio lista immagini. Errore: {0}", ex);
                System.Diagnostics.Trace.TraceError("Errore invio lista immagini. Errore: {0}", ex.Message);
            }
        }
    }
}
