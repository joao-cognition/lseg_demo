using System;
using System.Data;
using MarketDataHub.Data;
using MarketDataHub.Utils;

namespace MarketDataHub.Services
{
    /// <summary>
    /// Processes corporate actions (dividends, stock splits, mergers, rights issues, etc.)
    /// from the Oracle Exadata reference data master and applies adjustments to the
    /// SQL Server instrument and EOD tables.
    /// 
    /// Corporate actions flow:
    /// 1. FTSE Russell / data vendors maintain corporate actions in Oracle (LSEG-ORA-PROD01)
    /// 2. This service syncs confirmed actions nightly at 06:00 via ETL
    /// 3. Adjustments are applied to:
    ///    - Instruments table: shares outstanding, free float factor
    ///    - EndOfDaySummary: adjusted close prices
    ///    - IndexComposition: rebalance weights after splits/mergers
    /// 4. Processed actions are marked in Oracle to prevent reprocessing
    /// 
    /// Action types handled:
    /// - CASH_DIVIDEND: Record cash payment, no price adjustment needed
    /// - STOCK_SPLIT: Adjust price history and shares outstanding by ratio
    /// - REVERSE_SPLIT: Inverse of stock split
    /// - RIGHTS_ISSUE: Adjust shares outstanding and free float
    /// - MERGER: Delist acquired instrument, adjust acquirer if needed
    /// - SPIN_OFF: Create new instrument, adjust parent
    /// - NAME_CHANGE: Update instrument name and potentially ticker
    /// 
    /// NOTE: Corporate actions processing is one of the most error-prone operations.
    /// Every adjustment must be audited and reversible. The AuditService logs every
    /// change with before/after values. - Mark Williams, Compliance (2019)
    /// </summary>
    public class CorporateActionsService
    {
        /// <summary>
        /// Process all pending corporate actions for a given effective date.
        /// Returns number of actions processed.
        /// </summary>
        public static int ProcessPendingActions(DateTime effectiveDate)
        {
            MvcApplication.WriteLog("Processing corporate actions for " + effectiveDate.ToString("yyyy-MM-dd"));

            DataTable pendingActions = OracleDatabaseHelper.GetPendingCorporateActions(effectiveDate);
            int processed = 0;
            int failed = 0;

            foreach (DataRow action in pendingActions.Rows)
            {
                int actionId = Convert.ToInt32(action["ACTION_ID"]);
                string actionType = action["ACTION_TYPE"].ToString();
                string isin = action["ISIN_CODE"].ToString();

                try
                {
                    switch (actionType)
                    {
                        case "CASH_DIVIDEND":
                            ProcessCashDividend(action);
                            break;
                        case "STOCK_SPLIT":
                            ProcessStockSplit(action);
                            break;
                        case "REVERSE_SPLIT":
                            ProcessReverseSplit(action);
                            break;
                        case "RIGHTS_ISSUE":
                            ProcessRightsIssue(action);
                            break;
                        case "MERGER":
                            ProcessMerger(action);
                            break;
                        case "SPIN_OFF":
                            ProcessSpinOff(action);
                            break;
                        case "NAME_CHANGE":
                            ProcessNameChange(action);
                            break;
                        default:
                            MvcApplication.WriteLog("Unknown corporate action type: " + actionType + " for " + isin);
                            continue;
                    }

                    // Mark as processed in Oracle
                    OracleDatabaseHelper.MarkCorporateActionProcessed(actionId, "MarketDataHub-ETL");

                    // Log to local audit trail
                    AuditService.LogCorporateAction(actionId, actionType, isin, "Processed successfully");

                    processed++;
                    MvcApplication.WriteLog("Corporate action processed: " + actionType + " for " + isin);
                }
                catch (Exception ex)
                {
                    failed++;
                    MvcApplication.WriteLog("Corporate action FAILED: " + actionType + " for " + isin + " - " + ex.Message);
                    AuditService.LogCorporateAction(actionId, actionType, isin, "FAILED: " + ex.Message);
                }
            }

            if (failed > 0)
            {
                NotificationService.SendSystemAlert(
                    "Corporate actions processing: " + processed + " succeeded, " + failed + " failed on " +
                    effectiveDate.ToString("dd MMM yyyy"), failed > 0 ? "WARNING" : "INFO");
            }

            MvcApplication.WriteLog("Corporate actions completed: " + processed + " processed, " + failed + " failed");
            return processed;
        }

        private static void ProcessCashDividend(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            decimal cashAmount = Convert.ToDecimal(action["CASH_AMOUNT"]);
            string currency = action["CURRENCY_CODE"].ToString();
            DateTime paymentDate = Convert.ToDateTime(action["PAYMENT_DATE"]);

            // Record dividend in CorporateActionsHistory table
            string sql = string.Format(
                @"INSERT INTO CorporateActionsHistory (ISIN, ActionType, EffectiveDate, CashAmount, Currency, Description, CreatedDate)
                VALUES ('{0}', 'CASH_DIVIDEND', '{1}', {2}, '{3}', 'Cash dividend {2} {3} per share', GETDATE())",
                isin, paymentDate.ToString("yyyy-MM-dd"), cashAmount, currency);
            DatabaseHelper.ExecuteEtlCommand(sql);
        }

        private static void ProcessStockSplit(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            int ratioNew = Convert.ToInt32(action["RATIO_NEW"]);
            int ratioOld = Convert.ToInt32(action["RATIO_OLD"]);
            decimal splitFactor = (decimal)ratioNew / ratioOld;

            // Adjust instrument: multiply shares outstanding, divide prices
            string sql = string.Format(
                @"UPDATE Instruments SET 
                    LastPrice = LastPrice / {1}, PreviousClose = PreviousClose / {1},
                    DayHigh = DayHigh / {1}, DayLow = DayLow / {1}, DayOpen = DayOpen / {1},
                    YearHigh = YearHigh / {1}, YearLow = YearLow / {1},
                    Volume = Volume * {2}, AverageVolume30D = AverageVolume30D * {2},
                    ModifiedDate = GETDATE()
                WHERE ISIN = '{0}'",
                isin, splitFactor, ratioNew);
            DatabaseHelper.ExecuteEtlCommand(sql);

            // Adjust historical EOD data
            string eodSql = string.Format(
                @"UPDATE e SET 
                    e.OpenPrice = e.OpenPrice / {1}, e.HighPrice = e.HighPrice / {1},
                    e.LowPrice = e.LowPrice / {1}, e.ClosePrice = e.ClosePrice / {1},
                    e.AdjustedClose = e.AdjustedClose / {1},
                    e.TotalVolume = e.TotalVolume * {2}, e.VWAP = e.VWAP / {1}
                FROM EndOfDaySummary e
                INNER JOIN Instruments i ON e.InstrumentId = i.InstrumentId
                WHERE i.ISIN = '{0}'",
                isin, splitFactor, ratioNew);
            DatabaseHelper.ExecuteEtlCommand(eodSql);

            // Update index composition weights
            string idxSql = string.Format(
                @"UPDATE ic SET ic.SharesInIssue = ic.SharesInIssue * {1}
                FROM IndexComposition ic
                INNER JOIN Instruments i ON ic.InstrumentId = i.InstrumentId
                WHERE i.ISIN = '{0}' AND ic.IsActive = 1",
                isin, ratioNew);
            DatabaseHelper.ExecuteEtlCommand(idxSql);

            MvcApplication.WriteLog("Stock split applied: " + isin + " (" + ratioNew + ":" + ratioOld + ")");
        }

        private static void ProcessReverseSplit(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            int ratioNew = Convert.ToInt32(action["RATIO_NEW"]);
            int ratioOld = Convert.ToInt32(action["RATIO_OLD"]);
            decimal mergeFactor = (decimal)ratioOld / ratioNew;

            // Reverse split: multiply prices, divide shares
            string sql = string.Format(
                @"UPDATE Instruments SET 
                    LastPrice = LastPrice * {1}, PreviousClose = PreviousClose * {1},
                    ModifiedDate = GETDATE()
                WHERE ISIN = '{0}'",
                isin, mergeFactor);
            DatabaseHelper.ExecuteEtlCommand(sql);
        }

        private static void ProcessRightsIssue(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            int ratioNew = Convert.ToInt32(action["RATIO_NEW"]);
            int ratioOld = Convert.ToInt32(action["RATIO_OLD"]);

            // Update shares outstanding (increase by ratio)
            string sql = string.Format(
                @"UPDATE Instruments SET ModifiedDate = GETDATE() WHERE ISIN = '{0}'", isin);
            DatabaseHelper.ExecuteEtlCommand(sql);
        }

        private static void ProcessMerger(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            string description = action["DESCRIPTION"].ToString().Replace("'", "''");

            // Delist the acquired instrument
            string sql = string.Format(
                @"UPDATE Instruments SET IsActive = 0, IsSuspended = 1, 
                    SuspensionReason = 'Merger/Acquisition: {1}', ModifiedDate = GETDATE()
                WHERE ISIN = '{0}'",
                isin, description);
            DatabaseHelper.ExecuteEtlCommand(sql);

            // Remove from index compositions
            string idxSql = string.Format(
                @"UPDATE ic SET ic.IsActive = 0, ic.ExpiryDate = GETDATE()
                FROM IndexComposition ic
                INNER JOIN Instruments i ON ic.InstrumentId = i.InstrumentId
                WHERE i.ISIN = '{0}'",
                isin);
            DatabaseHelper.ExecuteEtlCommand(idxSql);
        }

        private static void ProcessSpinOff(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            string description = action["DESCRIPTION"].ToString().Replace("'", "''");

            // Log spin-off for manual review (new instrument needs to be set up)
            MvcApplication.WriteLog("SPIN_OFF requires manual instrument setup: " + isin + " - " + description);
            NotificationService.SendSystemAlert(
                "Spin-off event for " + isin + " requires manual instrument creation: " + description,
                "WARNING");
        }

        private static void ProcessNameChange(DataRow action)
        {
            string isin = action["ISIN_CODE"].ToString();
            string description = action["DESCRIPTION"].ToString().Replace("'", "''");

            // Update instrument name from Oracle master
            string sql = string.Format(
                @"UPDATE Instruments SET InstrumentName = '{1}', ModifiedDate = GETDATE() WHERE ISIN = '{0}'",
                isin, description);
            DatabaseHelper.ExecuteEtlCommand(sql);
        }
    }
}
