//------------------------------------------------------------------------------
// <auto-generated>
//     此代码由工具生成
//     如果重新生成代码，将丢失对此文件所做的更改。
// </auto-generated>
//------------------------------------------------------------------------------
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CURELab.SignLanguage.RecognitionSystem
{
    public class VisualFeatureModule
    {
        protected RecognitionController m_recognitionController;
        protected DataWarehouse m_dataWarehouse;
        public VisualFeatureModule() { }
        public VisualFeatureModule(RecognitionController recognitionController)
        {
            m_recognitionController = recognitionController;
            m_dataWarehouse = m_recognitionController.m_dataProcessor.m_dataWarehouse;
        }

        public virtual void OnDataTransfer(Object sender, DataTransferEventArgs args)
        {
            Console.WriteLine("visual callback:" + args.m_data);
        }
    }
}


