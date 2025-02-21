﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using ConnectorGrasshopper.Extras;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Speckle.Core.Models;
using Logging = Speckle.Core.Logging;
using Utilities = ConnectorGrasshopper.Extras.Utilities;

namespace ConnectorGrasshopper.Objects
{
  public class ExtendSpeckleObjectByKeyValueTaskComponent : SelectKitTaskCapableComponentBase<Base>
  {
    public ExtendSpeckleObjectByKeyValueTaskComponent() : base("Extend Speckle Object by Key/Value", "ESO K/V",
      "Extend a current object with key/value pairs", ComponentCategories.PRIMARY_RIBBON, ComponentCategories.OBJECTS)
    {
    }

    public override Guid ComponentGuid => new Guid("0D862057-254F-40C2-AC4A-9D163BB1E24B");
    protected override Bitmap Icon => Properties.Resources.ExtendSpeckleObjectByKeyValue;
    public override GH_Exposure Exposure => GH_Exposure.tertiary;

    protected override void RegisterInputParams(GH_InputParamManager pManager)
    {
      pManager.AddParameter(new SpeckleBaseParam("Speckle Object", "O", "Speckle object to extend.",
        GH_ParamAccess.item));
      pManager.AddTextParameter("Keys", "K", "List of keys", GH_ParamAccess.list);
      pManager.AddGenericParameter("Values", "V", "List of values", GH_ParamAccess.tree);
    }

    protected override void RegisterOutputParams(GH_OutputParamManager pManager)
    {
      pManager.AddParameter(new SpeckleBaseParam("Speckle Object", "O",
        "Extended Speckle object.", GH_ParamAccess.item));
    }

    protected override void SolveInstance(IGH_DataAccess DA)
    {
      if (InPreSolve)
      {
        GH_SpeckleBase @base = null;
        var keys = new List<string>();
        var valueTree = new GH_Structure<IGH_Goo>();

        DA.GetData(0, ref @base);
        DA.GetDataList(1, keys);
        DA.GetDataTree(2, out valueTree);

        if (DA.Iteration == 0)
        {
          Logging.Analytics.TrackEvent(Logging.Analytics.Events.NodeRun, new Dictionary<string, object>() { { "name", "Extend Object By Key Value" } });
        }


        TaskList.Add(Task.Run(() => DoWork(@base.Value.ShallowCopy(), keys, valueTree)));
        return;
      }

      if (Converter != null)
      {
        foreach (var error in Converter.Report.ConversionErrors)
        {
          AddRuntimeMessage(GH_RuntimeMessageLevel.Warning,
            error.Message + ": " + error.InnerException?.Message);
        }
        Converter.Report.ConversionErrors.Clear();
      }

      if (!GetSolveResults(DA, out var result))
      {
        // Normal mode not supported
        return;
      }

      if (result != null) DA.SetData(0, result);
    }

    public Base DoWork(Base @base, List<string> keys, GH_Structure<IGH_Goo> valueTree)
    {
      try
      {
        // 👉 Checking for cancellation!
        if (CancelToken.IsCancellationRequested) return null;

        // Create a path from the current iteration
        var searchPath = new GH_Path(RunCount - 1);

        // Grab the corresponding subtree from the value input tree.
        var subTree = Utilities.GetSubTree(valueTree, searchPath);
        // Find the list or subtree belonging to that path
        if (valueTree.PathExists(searchPath) || valueTree.Paths.Count == 1)
        {
          var list = valueTree.Paths.Count == 1 ? valueTree.Branches[0] : valueTree.get_Branch(searchPath);
          // We got a list of values
          var ind = 0;
          var hasErrors = false;
          keys.ForEach(key =>
          {
            if (ind < list.Count)
              try
              {
                if (Converter != null)
                  @base[key] = Utilities.TryConvertItemToSpeckle(list[ind], Converter);
                else
                  @base[key] = list[ind];
              }
              catch (Exception e)
              {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                hasErrors = true;
              }

            ind++;
          });
          if (hasErrors) @base = null;
        }
        else
        {
          // We got a tree of values

          // Create the speckle object with the specified keys
          var index = 0;
          var hasErrors = false;
          keys.ForEach(key =>
          {
            var itemPath = new GH_Path(index);

            var branch = subTree.get_Branch(itemPath);
            if (branch != null)
            {
              var objs = new List<object>();
              foreach (var goo in branch)
                if (Converter != null)
                  objs.Add(Utilities.TryConvertItemToSpeckle(goo, Converter));
                else
                  objs.Add(goo);

              if (objs.Count > 0)
                try
                {
                  @base[key] = objs;
                }
                catch (Exception e)
                {
                  AddRuntimeMessage(GH_RuntimeMessageLevel.Error, e.Message);
                  hasErrors = true;
                }
            }

            index++;
          });

          if (hasErrors) @base = null;
        }

        return @base;
      }
      catch (Exception e)
      {
        // If we reach this, something happened that we weren't expecting...
        Logging.Log.CaptureException(e);
        AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Something went terribly wrong... " + e.Message);
        return null;
      }
    }
  }
}
